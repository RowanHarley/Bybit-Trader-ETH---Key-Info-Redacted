using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Websocket.Client;

namespace Bybit_Trader__ETH_
{
    #region Classes
    public class BidAsk
    {
        public double bid_price { get; set; }
        public double ask_price { get; set; }
    }
    public class ConnectionErrorCheck
    {
        public bool success { get; set; }
        public string ret_msg { get; set; }
        public string conn_id { get; set; }
    }
    public class Candle
    {
        public double High { get; set; }
        public double Low { get; set; }
        public double Open { get; set; }
        public double Close { get; set; }
        public double AdvDecVal { get; set; }
        public double TR { get; set; }
        public double ATR { get; set; }
        public string Dir { get; set; }
    }
    public class Trade
    {
        public double size { get; set; }
        public double id { get; set; }
        public long create_time { get; set; }
        public long create_time_ms { get; set; }
        public double price { get; set; }
        public string contract { get; set; }
    }
    public class RecentTrade
    {
        public int size { get; set; }
        public int id { get; set; }
        public long create_time_ms { get; set; }
        public long create_time { get; set; }
        public string contract { get; set; }
        public double price { get; set; }
    }
    public class WalletResult
    {
        public int wallet_balance { get; set; }
        public int available_balance { get; set; }
    }
    public class Data
    {
        public BookOrder[] order_book { get; set; }
        public BookOrder[] delete { get; set; }
        public BookOrder[] update { get; set; }
        public BookOrder[] insert { get; set; }
        public string symbol { get; set; }
        public string tick_direction { get; set; }
        public string price { get; set; }
        public string size { get; set; }
        public string timestamp { get; set; }
        public string trade_time_ms { get; set; }
        public string side { get; set; }
        public string trade_id { get; set; }

    }
    public class Positions
    {
        public string topic { get; set; }
        public string action { get; set; }
        public Position[] data { get; set; }
    }
    public class Position
    {
        public string side { get; set; }
        public double size { get; set; }
        public double entry_price { get; set; }
    }
    public class HistTrade
    {
        private double _pnl;
        public double qty { get; set; }
        public string order_type { get; set; }
        public double closed_pnl { get { return _pnl + exec_value(); } set { _pnl = value; } }
        public double avg_entry_price { get; set; }
        public double avg_exit_price { get; set; }
        public double exec_value()
        {
            if (order_type == "Limit")
            {
                return (qty * avg_entry_price * -0.025 / 100) + (qty * avg_exit_price * -0.025 / 100);
            }
            else
            {
                return (qty * avg_entry_price * -0.025 / 100) + (qty * avg_exit_price * 0.075 / 100);
            }
        }
    }
    public class TradeHistory
    {
        public string topic { get; set; }
        public HistTrade[] data { get; }
    }
    public class BookOrder
    {
        public double price { get; set; }
        public string symbol { get; set; }
        public int id { get; set; }
        public string side { get; set; }
        public double size { get; set; }
    }
    public class ParsedResult
    {
        public string topic { get; set; }
        public string type { get; set; }
        public Data data { get; set; }
        public string cross_seq { get; set; }
        public string timestamp_e6 { get; set; }
        public string success { get; set; }
        public string ret_msg { get; set; }
    }
    public class ParsedTradeResult
    {
        public string channel { get; set; }
        public string @event { get; set; }
        public int time { get; set; }
        public Trade[] result { get; set; }
    }
    public class APIResponse
    {
        public int ret_code { get; set; }
        public string ret_msg { get; set; }
        public string ext_code { get; set; }
        public string ext_info { get; set; }
        public object result { get; set; }
        public double time_now { get; set; }
        public int rate_limit_status { get; set; }
        public long rate_limit_reset_ms { get; set; }
        public int rate_limit { get; set; }
    }
    public class Orders
    {
        public string topic { get; set; }
        public string action { get; set; }
        public List<Order> data { get; set; }
    }
    public class Order
    {
        public string order_id { get; set; }
        public string symbol { get; set; }
        public string side { get; set; }
        public double price { get; set; }
        public double qty { get; set; }
        public string create_time { get; set; }
    }
    public class Pong
    {
        public bool success { get; set; }
        public string ret_msg { get; set; }
    }
    public class ParsedPrivate
    {
        public string topic { get; set; }
        public object data { get; set; }
    }
    #endregion
    public class AlgoFunctions
    {

        public bool CircuitBreakerHit { get; set; } = false;
        public DateTime LowMovementFinish { get; set; } = DateTime.UnixEpoch;

        public readonly double MaxTradeRisk = 2;
        private readonly int[] NormalError = new int[] { 130010, 130057 };
        public bool closingPos = false;
        public double beginSlPrice = 0;
        public Trade last;
        public double LastAsk;
        private long LastTradeCheck;
        public Order CurrentCloseOrder;
        public Order CurrentModOrder;

        HttpClient AWSClient = new HttpClient();
        AmazonCloudWatchClient AWSCloud = new AmazonCloudWatchClient("API_KEY", "API_SECRET");

        #region These Variables will need to be updated if Application stopped after first initialisation!
        public double CurrBalance = 1000;
        private double StartAccBalance = 1000;
        public double TotalRebate = 0; // Start Value: 0
        public int Tradesat0Risk { get; set; } = 0;
        public int lossStreak { get; set; } = 0;
        private double MaxAccVal = 1000;
        private int DecreaseNum = 0;
        public double DailyLossLim { get; set; } = 12;
        public double CurrMaxRisk = 2; // This risk can be altered based on tolerance
        #endregion

        public string URL = "https://api.bytick.com";
        public enum OrderCancelType
        {
            Filled,
            Cancelled,
            Open
        }
        private readonly string GateioURL = "https://api.gateio.ws/api/v4"; // Backtesting was carried out on Gate.io data, trading on Bybit, so data from Gate.io is used to find entry point
        private readonly string CreateOrderStr = "/private/linear/order/create";
        private readonly string ModifyOrderStr = "/private/linear/order/replace";
        private readonly string CancelAllOrdersStr = "/private/linear/order/cancel-all";
        private readonly string WalletStr = "/v2/private/wallet/balance";
        private readonly string PastTradesStr = "/private/linear/trade/closed-pnl/list";
        private readonly string GateIOTrades = "/futures/usdt/trades";
        private readonly string TradeSearch = "/private/linear/order/search";
        private readonly string GetPosStr = "/private/linear/position/list";
        private readonly string FundingRateStr = "/public/linear/funding/prev-funding-rate";
        private readonly string GetBidAskStr = "/v2/public/tickers";
        public DateTime fundingTime;
        public System.Timers.Timer checkModOrder = new System.Timers.Timer() { Interval = 107 }; // Limit @ 100 ms

        HttpClient client = new HttpClient();
        public double CurrentPositionSize = 0;
        public bool CurrentPosFilled = false;
        public Order TPOrder = null;
        private double LimDecrease = 0.5; // This is the change to the daily loss limit each time it is hit
        private double BuyPrice;
        public double CurrentPositionEntry = double.NaN;
        public long modReqTime;
        public double TimeBetweenCandles = 60; // In backtesting, I have found that certain times produce consistently better results than others
        public int numReqs = 0;


        public readonly string APIKey = "BYBIT_KEY";
        public readonly string Secret = "BYBIT_SECRET";

        public async Task CheckModOrders()
        {
            if (IsOrderCancelled(CurrentModOrder.order_id) == OrderCancelType.Cancelled)
            {
                await CreateNewOrder("Buy", isClosing: true, reduceOnly: true);
            }
        }
        public void GetPrevTrades(string symbol = "ETHUSDT")
        {
            bool TradesRecieved = false;
            double lastTradePnL = 0;
            while (!TradesRecieved)
            {
                if (LastTradeCheck != 0)
                {
                    Task.Delay(4000).Wait();
                }
                var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                string expires;
                string param_str = $"api_key={APIKey}&limit=50" + (LastTradeCheck > 0 ? $"&start_time={LastTradeCheck}" : "") + $"&symbol={symbol}&timestamp={CurrTime}";
                expires = CreateSignature(Secret, param_str);
                param_str += $"&sign={expires}";

                var httpRes = client.GetAsync(URL + PastTradesStr + "?" + param_str).Result;

                if (httpRes.Content != null)
                {
                    APIResponse result = JsonConvert.DeserializeObject<APIResponse>(httpRes.Content.ReadAsStringAsync().Result);

                    if (result.ret_code == 10006)
                    {
                        Console.WriteLine("Delaying until next reset");
                        Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                    }
                    else if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                    {
                        Console.WriteLine("Fatal error " + result.ret_code + " @ GetPrevTrades() ! Retrying operation!" + result.ret_msg);
                    }
                    else
                    {
                        JToken t = JsonConvert.DeserializeObject<JToken>(httpRes.Content.ReadAsStringAsync().Result);
                        List<HistTrade> tra = t["result"]["data"].ToObject<List<HistTrade>>();
                        if (LastTradeCheck != 0)
                        {
                            foreach (HistTrade tr in tra)
                            {
                                lastTradePnL += tr.closed_pnl;
                                TotalRebate -= tr.exec_value();
                            }
                        }
                        else
                        {
                            LastTradeCheck = CurrTime / 1000;
                            return;
                        }
                        Console.WriteLine("Total PnL: " + lastTradePnL + ", Fees: " + TotalRebate);
                        CurrentPositionSize = 0;
                        CurrentPositionEntry = double.NaN;
                        CurrentPosFilled = false;
                        TPOrder = null;
                        closingPos = false;
                        CurrentCloseOrder = null;
                        CurrentModOrder = null;
                        beginSlPrice = 0;
                        CurrBalance += lastTradePnL;
                        LastTradeCheck = CurrTime / 1000;
                        if (lastTradePnL <= 0)
                        {
                            lossStreak++;

                            CurrMaxRisk -= 0.5;
                        }
                        else
                        {
                            if (StartAccBalance < CurrBalance)
                            {
                                StartAccBalance = CurrBalance;
                            }
                            if (MaxAccVal < CurrBalance)
                            {
                                MaxAccVal = CurrBalance;
                            }
                            lossStreak = 0;
                            CurrMaxRisk = MaxTradeRisk;
                        }
                        if (TotalRebate > CurrBalance)
                        {
                            CurrBalance += TotalRebate / 2;
                            TotalRebate /= 2;
                        }
                        if (CurrBalance < 100)
                        {
                            var availRebate = (MaxAccVal - CurrBalance) <= TotalRebate ? MaxAccVal - CurrBalance : TotalRebate;
                            CurrBalance += availRebate;
                            TotalRebate -= availRebate;
                            MaxAccVal = CurrBalance;
                        }
                        if (CurrBalance <= ((100 - DailyLossLim) / 100) * StartAccBalance)
                        {
                            CircuitBreakerHit = true;
                            LowMovementFinish = DateTime.UtcNow.AddHours(6); // Cuts off trading for 6 hours
                            StartAccBalance = CurrBalance;
                            DailyLossLim -= LimDecrease;
                            DecreaseNum++;
                            if (TotalRebate > CurrBalance)
                            {
                                CurrBalance += TotalRebate / 2;
                                TotalRebate /= 2;
                            }
                            if (CurrBalance < 100)
                            {
                                var availRebate = (MaxAccVal - CurrBalance) <= TotalRebate ? MaxAccVal - CurrBalance : TotalRebate;
                                CurrBalance += availRebate;
                                TotalRebate -= availRebate;
                                MaxAccVal = CurrBalance;
                            }
                            if (DailyLossLim <= MaxTradeRisk + 1)
                            {
                                DailyLossLim += DecreaseNum * LimDecrease;
                                DecreaseNum = 0;
                                CircuitBreakerHit = false;
                            }
                        }

                        TradesRecieved = true;
                    }
                }
                else
                {
                    Console.WriteLine("No response!");
                }
            }
        }
        public void CreateBeginningCandles(List<Candle> Candles, List<Trade> t)
        {
            long time = DateTimeOffset.UtcNow.AddSeconds(-TimeBetweenCandles * 18 - (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % TimeBetweenCandles)).ToUnixTimeSeconds();
            int i = 0;
            while (true)
            {
                if (i > 10)
                {
                    Environment.Exit(0);
                }
                if (time > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    break;
                }
                string param = $"?contract=ETH_USDT&limit=1000&from={time}&to={time + TimeBetweenCandles}";
                using var client = new HttpClient();
                var res = client.GetAsync(GateioURL + GateIOTrades + param).Result;
                t = JsonConvert.DeserializeObject<List<Trade>>(res.Content.ReadAsStringAsync().Result);
                t.Reverse();
                // Console.WriteLine(t.Last().create_time + ", " + DateTimeOffset.FromUnixTimeSeconds(t.Last().create_time).DateTime);
                if (t.Last().create_time <= time + TimeBetweenCandles)
                {
                    i = 0;
                    if (time + TimeBetweenCandles > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    {
                        break;
                    }
                    CreateCandle(Candles, t);
                    time += Convert.ToInt64(TimeBetweenCandles);
                }
                else
                {
                    Console.WriteLine("Recieved wrong results");
                    i++;

                }
            }
            Console.WriteLine("Got Trades");
        }
        public void SendMetrics(List<Candle> cdls)
        {
            if (closingPos)
                return;

            var dimension = new Dimension
            {
                Name = "Bybit Metrics",
                Value = "C# Values"
            };
            MetricDatum LastAdvDec = new MetricDatum
            {
                Dimensions = new List<Dimension>(),
                MetricName = "Advance Decline",
                StatisticValues = new StatisticSet(),
                TimestampUtc = DateTime.UtcNow,
                Unit = StandardUnit.Count,
                Value = cdls.Last().AdvDecVal
            };
            MetricDatum Bal = new MetricDatum
            {
                Dimensions = new List<Dimension>(),
                MetricName = "Account Balance",
                StatisticValues = new StatisticSet(),
                TimestampUtc = DateTime.UtcNow,
                Unit = StandardUnit.Count,
                Value = CurrBalance
            };
            MetricDatum CurrMRisk = new MetricDatum
            {
                Dimensions = new List<Dimension>(),
                MetricName = "Current Max Risk",
                StatisticValues = new StatisticSet(),
                TimestampUtc = DateTime.UtcNow,
                Unit = StandardUnit.Count,
                Value = CurrMaxRisk,
            };
            MetricDatum TotRebate = new MetricDatum
            {
                Dimensions = new List<Dimension>(),
                MetricName = "Total Rebate Value",
                StatisticValues = new StatisticSet(),
                TimestampUtc = DateTime.UtcNow,
                Unit = StandardUnit.Count,
                Value = TotalRebate
            };
            MetricDatum isTradeStopOn = new MetricDatum
            {
                Dimensions = new List<Dimension>(),
                MetricName = "Is trading stop on?",
                StatisticValues = new StatisticSet(),
                TimestampUtc = DateTime.UtcNow,
                Unit = StandardUnit.Count,
                Value = (DateTime.UtcNow > LowMovementFinish ? 0 : 1)
            };
            MetricDatum DecrNum = new MetricDatum
            {
                Dimensions = new List<Dimension>(),
                MetricName = "DecreaseNum (Number of stops)",
                StatisticValues = new StatisticSet(),
                TimestampUtc = DateTime.UtcNow,
                Unit = StandardUnit.Count,
                Value = DecreaseNum
            };
            MetricDatum ZeroRiskTrades = new MetricDatum
            {
                Dimensions = new List<Dimension>(),
                MetricName = "0 Risk Trades",
                StatisticValues = new StatisticSet(),
                TimestampUtc = DateTime.UtcNow,
                Unit = StandardUnit.Count,
                Value = Tradesat0Risk
            };
            MetricDatum MaxAcc = new MetricDatum
            {
                Dimensions = new List<Dimension>(),
                MetricName = "Max Account Value",
                StatisticValues = new StatisticSet(),
                TimestampUtc = DateTime.UtcNow,
                Unit = StandardUnit.Count,
                Value = MaxAccVal
            };
            MetricDatum LossStreak = new MetricDatum
            {
                Dimensions = new List<Dimension>(),
                MetricName = "Loss Streak",
                StatisticValues = new StatisticSet(),
                TimestampUtc = DateTime.UtcNow,
                Unit = StandardUnit.Count,
                Value = lossStreak
            };

            var request = new PutMetricDataRequest
            {
                MetricData = new List<MetricDatum>() { TotRebate, isTradeStopOn, Bal, LossStreak, CurrMRisk, LastAdvDec, MaxAcc, DecrNum, ZeroRiskTrades },
                Namespace = "Bybit Trader Custom Metrics"
            };
            AWSCloud.PutMetricDataAsync(request);
        }
        public bool CheckFundingRate()
        {
            bool FundingRateFound = false;
            int t = 0;
            while (!FundingRateFound)
            {
                if (t > 6)
                {
                    Console.WriteLine("Failed to get funding rate!");
                    ClosePositionQuick();
                }
                string param_str = $"symbol=ETHUSDT";
                var httpRes = client.GetAsync(URL + FundingRateStr + "?" + param_str).Result;
                if (httpRes.Content != null)
                {
                    APIResponse result = JsonConvert.DeserializeObject<APIResponse>(httpRes.Content.ReadAsStringAsync().Result);
                    if (result.ret_code == 10006)
                    {
                        Console.WriteLine("Delaying until next reset");
                        Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                    }
                    else if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                    {
                        Console.WriteLine("Fatal error " + result.ret_code + " @ GetFundingRate() ! Retrying operation!" + result.ret_msg);
                        ClosePositionQuick();
                    }
                    else
                    {
                        JToken json = JsonConvert.DeserializeObject<JToken>(httpRes.Content.ReadAsStringAsync().Result);
                        var funding = double.Parse(json["result"]["funding_rate"].ToString());
                        if (funding >= 0)
                        {
                            return true;
                        }
                        FundingRateFound = true;
                    }
                }
                else
                {
                    Console.WriteLine("No response! " + httpRes.StatusCode);
                }
                t++;
            }
            return false;
        }
        public OrderCancelType IsOrderCancelled(string id)
        {
            bool OrderChecked = false;
            while (!OrderChecked)
            {
                var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                string expires;
                string param_str = $"api_key={APIKey}&order_id={id}&recv_window=10000&symbol=ETHUSDT&timestamp={CurrTime}";
                expires = CreateSignature(Secret, param_str);
                param_str += $"&sign={expires}";
                var httpRes = client.GetAsync(URL + TradeSearch + "?" + param_str).Result;
                if (httpRes.Content != null)
                {
                    APIResponse result = JsonConvert.DeserializeObject<APIResponse>(httpRes.Content.ReadAsStringAsync().Result);
                    if (result.ret_code == 10006)
                    {
                        Console.WriteLine("Delaying until next reset");
                        Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                    }
                    else if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                    {
                        Console.WriteLine("Fatal error " + result.ret_code + " @IsOrderCancelled() ! Retrying operation!" + result.ret_msg);
                        ClosePositionQuick();
                    }
                    else
                    {
                        JToken json = JsonConvert.DeserializeObject<JToken>(httpRes.Content.ReadAsStringAsync().Result);
                        var stat = json["result"]["order_status"].ToString();
                        if (stat == "New" || stat == "Created" || stat == "PartiallyFilled")
                        {
                            return OrderCancelType.Open;
                        }
                        else if (stat == "Filled")
                        {
                            return OrderCancelType.Filled;
                        }
                        OrderChecked = true;
                    }
                }
                else
                {
                    Console.WriteLine("No response!");
                    ClosePositionQuick();
                }

            }
            return OrderCancelType.Cancelled;
        }
        public void CreateCancelTPOrder()
        {
            bool isOrderCreated = false;
            int tries = 0;
            if (CurrentPositionSize == 0)
            {
                Console.WriteLine("Attempted to create TP order on 0 position");
                return;
            }
            while (!isOrderCreated)
            {

                var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                string expires;
                double Amount = CurrentPositionSize;
                Dictionary<string, object> param = new Dictionary<string, object>
                {
                    ["api_key"] = APIKey,
                    ["close_on_trigger"] = false,
                    ["order_type"] = "Limit",
                    ["price"] = (CurrentPositionEntry - 2.5 * beginSlPrice) <= LastAsk - 0.05 ? Math.Round((CurrentPositionEntry - 2.5 * beginSlPrice) * 2, 1) / 2 : Math.Round((LastAsk - 0.05) * 2, 2) / 2,
                    ["qty"] = Amount,
                    ["reduce_only"] = true,
                    ["side"] = "Buy",
                    ["symbol"] = "ETHUSDT",
                    ["time_in_force"] = "GoodTillCancel",
                    ["timestamp"] = CurrTime,
                    ["recv_window"] = 10000
                };
                string param_str = $"api_key={APIKey}&close_on_trigger=false&order_type=Limit&price={param["price"]}&qty={Amount}&recv_window=10000&reduce_only=true&side=Buy&symbol=ETHUSDT&time_in_force=GoodTillCancel&timestamp={param["timestamp"]}";
                expires = CreateSignature(Secret, param_str);
                param.OrderBy(x => x.Key);
                param.Add("sign", expires);
                var json = JsonConvert.SerializeObject(param);
                var newjson = json.Replace(".0,", ",");

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{URL}{CreateOrderStr}");
                request.Content = new StringContent(newjson, Encoding.UTF8, "application/json");
                using var client = new HttpClient();
                HttpResponseMessage res = client.SendAsync(request).Result;
                if (res.IsSuccessStatusCode)
                {
                    JToken response = JsonConvert.DeserializeObject<JToken>(res.Content.ReadAsStringAsync().Result);
                    APIResponse result = response.ToObject<APIResponse>();
                    if (result.ret_code == 10006)
                    {
                        Console.WriteLine("Delaying until next reset");
                        Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                    }
                    else if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                    {
                        Console.WriteLine("Fatal error " + result.ret_code + ": " + result.ret_msg + " @ CreateCancelTP() ! Retrying operation!" + " Amount: " + Amount);
                        tries++;
                        if (tries >= 4)
                        {
                            ClosePositionQuick();
                        }
                    }

                    else
                    {
                        TPOrder = response["result"].ToObject<Order>();
                        if (IsOrderCancelled(TPOrder.order_id) != OrderCancelType.Cancelled && CurrentModOrder == null)
                        {
                            isOrderCreated = true;
                        }
                        else
                        {
                            Console.WriteLine("No TP Order created! Mod Order is null/Order was cancelled");
                            return;
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No response!");
                    ClosePositionQuick();
                }
            }

        }
        public void ClosePositionQuick()
        {
            Console.WriteLine("Shutting down program!");
            CancelAllOrders();
            Candle cdl = new Candle();
            cdl.AdvDecVal = 0;
            if (CurrentPositionSize != 0)
            {
                bool PositionClosed = false;
                while (!PositionClosed)
                {
                    var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                    double Amount = CurrentPositionSize;
                    Dictionary<string, object> param = new Dictionary<string, object>
                    {
                        ["api_key"] = APIKey,
                        ["close_on_trigger"] = false,
                        ["order_type"] = "Market",
                        ["qty"] = Amount,
                        ["reduce_only"] = true,
                        ["side"] = "Buy",
                        ["symbol"] = "ETHUSDT",
                        ["time_in_force"] = "GoodTillCancel",
                        ["timestamp"] = CurrTime
                    };
                    string param_str = $"api_key={APIKey}&close_on_trigger=false&order_type=Market&qty={Amount}&reduce_only=true&side=Buy&symbol=ETHUSDT&time_in_force=GoodTillCancel&timestamp={CurrTime}";
                    string expires = CreateSignature(Secret, param_str);

                    param.Add("sign", expires);
                    var json = JsonConvert.SerializeObject(param);
                    var newjson = json.Replace(".0,", ",");

                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{URL}{CreateOrderStr}");
                    request.Content = new StringContent(newjson, Encoding.UTF8, "application/json");
                    using var client = new HttpClient();
                    HttpResponseMessage res = client.SendAsync(request).Result;
                    if (res.IsSuccessStatusCode)
                    {
                        JToken response = JsonConvert.DeserializeObject<JToken>(res.Content.ReadAsStringAsync().Result);
                        APIResponse result = response.ToObject<APIResponse>();
                        if (result.ret_code == 10006)
                        {
                            Console.WriteLine("Delaying until next reset");
                            Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                        }
                        if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                        {
                            Console.WriteLine("Fatal error " + result.ret_code + "@ ClosePositionQuick() ! Retrying operation!");
                        }
                        else
                        {
                            closingPos = true;
                            PositionClosed = true;

                            SendMetrics(new List<Candle>() { cdl });
                            Environment.Exit(0);
                        }
                    }
                    else
                    {
                        Console.WriteLine("No response!");
                    }
                }
            }
            SendMetrics(new List<Candle>() { cdl });
            Environment.Exit(0);
        }
        public void GetBidAsk()
        {
            string param_str = $"symbol=ETHUSDT";
            var httpRes = client.GetAsync(URL + FundingRateStr + "?" + param_str).Result;
            if (httpRes.Content != null)
            {
                APIResponse res = JsonConvert.DeserializeObject<APIResponse>(httpRes.Content.ReadAsStringAsync().Result);
                if (res.ret_code == 10006)
                {
                    Console.WriteLine("Delaying until next reset");
                    Task.Delay(Convert.ToInt32(res.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                }
                else if (res.ret_code != 0 && !NormalError.Contains(res.ret_code))
                {
                    Console.WriteLine("Fatal error " + res.ret_code + " @ GetFundingRate() ! Retrying operation!" + res.ret_msg);
                    ClosePositionQuick();
                }
                else
                {
                    JToken json = JsonConvert.DeserializeObject<JToken>(httpRes.Content.ReadAsStringAsync().Result);
                    List<BidAsk> list = json["result"].ToObject<List<BidAsk>>();
                    LastAsk = Convert.ToDouble(list.First().ask_price);
                }
            }
            else
            {
                Console.WriteLine("No Response!");
                ClosePositionQuick();
            }

        }
        public async Task CreateNewOrder(string Side, Candle lastCandle = null, bool isClosing = false, bool reduceOnly = false)
        {
            int t = 0;
            if (isClosing)
            {
                /*if (!tpCancelled)
                {
                    CancelOrder(TPOrder.order_id);
                    tpCancelled = true;
                }*/
                bool isOrderCreated = false;
                while (!isOrderCreated)
                {
                    var bestAsk = Math.Round((LastAsk - 0.05) * 2, 2) / 2;
                    int BuyPriceInt = Convert.ToInt32(bestAsk);
                    var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                    double Amount = CurrentPositionSize;
                    Dictionary<string, object> param = new Dictionary<string, object>
                    {
                        ["api_key"] = APIKey,
                        ["close_on_trigger"] = false,
                        ["order_type"] = "Limit",
                        ["price"] = (bestAsk % 1 == 0 ? BuyPriceInt : bestAsk),
                        ["qty"] = Amount,
                        ["reduce_only"] = true,
                        ["side"] = Side,
                        ["symbol"] = "ETHUSDT",
                        ["time_in_force"] = "PostOnly",
                        ["timestamp"] = CurrTime
                    };
                    string param_str = $"api_key={APIKey}&close_on_trigger=false&order_type=Limit&price={param["price"]}&qty={Amount}&reduce_only=true&side={Side}&symbol=ETHUSDT&time_in_force=PostOnly&timestamp={CurrTime}";
                    string expires = CreateSignature(Secret, param_str);

                    param.Add("sign", expires);
                    var json = JsonConvert.SerializeObject(param);
                    var newjson = json.Replace(".0,", ",");

                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{URL}{CreateOrderStr}");
                    request.Content = new StringContent(newjson, Encoding.UTF8, "application/json");
                    using var client = new HttpClient();
                    HttpResponseMessage res = await client.SendAsync(request);

                    if (res.IsSuccessStatusCode)
                    {
                        JToken response = JsonConvert.DeserializeObject<JToken>(await res.Content.ReadAsStringAsync());
                        APIResponse result = response.ToObject<APIResponse>();
                        if (result.ret_code == 130125)
                        {
                            Console.WriteLine("Error 130125");
                            CurrentPositionSize = CheckPosSize();
                            if (Double.IsNaN(CurrentPositionSize) || CurrentPositionSize == 0)
                            {
                                isOrderCreated = true;
                            }
                        }
                        else if (result.ret_code == 10006)
                        {
                            Console.WriteLine("Delaying until next reset");
                            Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                        }
                        else if (result.ret_code == 130010)
                        {
                            Console.WriteLine("Error 130010");
                            CurrentPositionSize = CheckPosSize();
                            if (Double.IsNaN(CurrentPositionSize) || CurrentPositionSize == 0)
                            {
                                isOrderCreated = true;
                            }
                        }
                        else if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                        {
                            Console.WriteLine("Fatal error " + result.ret_code + "@ CreateNewOrder() ! Retrying operation!" + result.ret_msg);
                            ClosePositionQuick();
                        }
                        else
                        {
                            CurrentModOrder = response["result"].ToObject<Order>();
                            Console.WriteLine("Modify Order created");
                            await Task.Delay(2);
                            if (!checkModOrder.Enabled)
                                checkModOrder.Start();

                            isOrderCreated = true;
                            /*if (!IsOrderCancelled(CurrentModOrder.order_id))
                            {
                                closingPos = true;
                                isOrderCreated = true;
                                CurrentPositionSize = Amount;
                                modReqTime = DateTime.UtcNow;
                            }
                            else
                            {
                                Console.WriteLine("Creating another mod order");
                            }*/

                        }
                    }
                    else
                    {
                        Console.WriteLine("No response!");
                        ClosePositionQuick();
                    }
                }
            }
            else
            {
                if (DateTime.UtcNow <= fundingTime && DateTime.UtcNow >= fundingTime.AddMinutes(-10))
                {
                    Console.WriteLine("Checking Funding rate in CreateOrder");
                    if (!CheckFundingRate())
                    {
                        return;
                    }
                    //fundingTime.AddHours(8);
                }

                if (CircuitBreakerHit && DateTime.UtcNow < LowMovementFinish)
                {
                    Console.WriteLine("Circuit Breaker hit inside CreateLimitOrder. Time: " + DateTimeOffset.FromUnixTimeMilliseconds(last.create_time_ms).UtcDateTime.ToString());
                    return;
                }
                if (Tradesat0Risk >= 3)
                {
                    CurrMaxRisk = MaxTradeRisk;
                    Tradesat0Risk = 0;
                    lossStreak = 0;
                }
                else if (lossStreak > 3)
                {
                    Tradesat0Risk++;
                    Console.WriteLine("New trade at 0 risk! " + Tradesat0Risk + " @ " + DateTime.UtcNow);
                    return;
                }
                bool isOrderCreated = false;
                while (!isOrderCreated)
                {
                    if (t > 10)
                    {
                        Console.WriteLine("Order couldn't be created!");
                        ClosePositionQuick();
                    }
                    var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                    string expires;
                    beginSlPrice = Math.Round(lastCandle.ATR * 2, 2, MidpointRounding.ToNegativeInfinity) / 2;
                    double BeginAmount = CurrMaxRisk * CurrBalance / (100 * beginSlPrice);
                    double Val = BeginAmount * LastAsk * 0.16 / 100;
                    double Amount;
                    if (t > 0)
                    {
                        GetBidAsk();
                    }
                    if (BeginAmount * LastAsk / (CurrBalance - Val + TotalRebate) <= 48)
                    {
                        Amount = Math.Round(CurrMaxRisk * CurrBalance / (100 * beginSlPrice), 3, MidpointRounding.ToNegativeInfinity);
                    }
                    else
                    {
                        BeginAmount = 48 * (CurrBalance + TotalRebate) / LastAsk;
                        //double Val = BeginAmount * LastAsk * 0.16 / 100;

                        Amount = Math.Round(48 * (CurrBalance + TotalRebate - Val) / LastAsk, 2, MidpointRounding.ToNegativeInfinity);
                    }
                    int BuyPriceInt = Convert.ToInt32(LastAsk);
                    BuyPrice = Math.Round(LastAsk * 2, 2) / 2;
                    double TP = Math.Round((BuyPrice - 2.5 * beginSlPrice) * 2, 2) / 2;
                    Dictionary<string, object> param = new Dictionary<string, object>
                    {

                        ["api_key"] = APIKey,
                        ["close_on_trigger"] = false,
                        ["order_type"] = "Limit",
                        ["price"] = (BuyPrice % 1 == 0 ? BuyPriceInt : BuyPrice),
                        ["qty"] = Amount,
                        ["reduce_only"] = false,
                        ["side"] = Side,
                        ["symbol"] = "ETHUSDT",
                        ["time_in_force"] = "PostOnly",
                        ["timestamp"] = CurrTime,
                        ["recv_window"] = 10000
                    };
                    string param_str = $"api_key={APIKey}&close_on_trigger=false&order_type=Limit&price={param["price"]}&qty={param["qty"]}&recv_window=10000&reduce_only=false&side={Side}&symbol=ETHUSDT&time_in_force=PostOnly&timestamp={param["timestamp"]}";
                    expires = CreateSignature(Secret, param_str);
                    param.OrderBy(x => x.Key);
                    param.Add("sign", expires);
                    var json = JsonConvert.SerializeObject(param);
                    var newjson = json.Replace(".0,", ",");

                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{URL}{CreateOrderStr}");
                    request.Content = new StringContent(newjson, Encoding.UTF8, "application/json");
                    using var client = new HttpClient();
                    HttpResponseMessage res = await client.SendAsync(request);
                    if (res.IsSuccessStatusCode)
                    {
                        JToken response = JsonConvert.DeserializeObject<JToken>(await res.Content.ReadAsStringAsync());
                        var result = response.ToObject<APIResponse>();
                        if (result.ret_code == 130021)
                        {
                            Console.WriteLine("Fatal error " + result.ret_code + ": " + result.ret_msg + " @ CreateOrder() ! Retrying operation!" + " Amount: " + Amount);
                            ClosePositionQuick();
                        }
                        else if (result.ret_code == 10006)
                        {
                            Console.WriteLine("Delaying until next reset");
                            Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                        }
                        else if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                        {
                            Console.WriteLine("Fatal error " + result.ret_code + ": " + result.ret_msg + " @ CreateOrder() ! Retrying operation!" + " Amount: " + Amount);
                            ClosePositionQuick();
                        }
                        else
                        {
                            CurrentCloseOrder = response["result"].ToObject<Order>();
                            if (IsOrderCancelled(CurrentCloseOrder.order_id) != OrderCancelType.Cancelled)
                            {
                                Console.WriteLine("Order successfully opened! Details: " + CurrentCloseOrder.price + ", Qty: " + CurrentCloseOrder.qty + ", SL: " + beginSlPrice);
                                CheckOrders(Amount);
                                isOrderCreated = true;
                            }
                            else
                            {
                                Console.WriteLine("Order was never created! Trying again");
                                t++;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("No response!");
                        ClosePositionQuick();
                    }
                }
            }

        }
        public void ModifyOrder(string symbol = "ETHUSDT")
        {
            bool isOrderModified = false;
            int t = 0;
            while (!isOrderModified)
            {
                if (t > 10)
                {
                    Console.WriteLine("Too many loops!");
                    ClosePositionQuick();
                }
                if (CurrentModOrder == null)
                {
                    Console.WriteLine("Mod Order null, position most likely closed");
                    closingPos = false;
                    return;
                }
                double bestAsk = Math.Round((LastAsk - 0.05) * 2, 2) / 2;
                var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                string expires;
                string id = CurrentModOrder.order_id;
                Dictionary<string, object> param = new Dictionary<string, object>
                {
                    ["api_key"] = APIKey,
                    ["order_id"] = id,
                    ["p_r_price"] = (bestAsk % 1 == 0 ? Convert.ToInt32(bestAsk) : bestAsk),
                    ["symbol"] = symbol,
                    ["timestamp"] = CurrTime
                };
                string param_str = $"api_key={APIKey}&order_id={id}&p_r_price={param["p_r_price"]}&symbol={symbol}&timestamp={CurrTime}";
                expires = CreateSignature(Secret, param_str);
                param.Add("sign", expires);
                var json = JsonConvert.SerializeObject(param);
                var newjson = json.Replace(".0,", ",");

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{URL}{ModifyOrderStr}");
                request.Content = new StringContent(newjson, Encoding.UTF8, "application/json");
                using var client = new HttpClient();
                HttpResponseMessage res = client.SendAsync(request).Result;
                if (res.Content != null)
                {
                    JToken resJ = JsonConvert.DeserializeObject<JToken>(res.Content.ReadAsStringAsync().Result);
                    APIResponse result = resJ.ToObject<APIResponse>();
                    if (result.ret_code == 130125)
                    {
                        Console.WriteLine("Order not modified! " + result.ret_code);
                    }
                    else if (result.ret_code == 10006)
                    {
                        Console.WriteLine("Delaying until next reset");
                        Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                    }
                    else if (result.ret_code == 130032)
                    {
                        Console.WriteLine("Eror 130032");
                        t++;
                    }
                    else if (result.ret_code == 130010 || result.ret_code == 130142)
                    {
                        double q = CheckPosSize();
                        Console.WriteLine("Error in modify order: " + result.ret_code + ", " + result.ret_msg);
                        if (q > 0)
                        {
                            CurrentPositionSize = q;
                            CreateNewOrder("Buy", isClosing: true, reduceOnly: true).Wait();
                        }
                        else
                        {
                            CurrentPositionSize = 0;
                            Position pos = new Position();
                            pos.size = 0;
                            pos.entry_price = CurrentPositionEntry;
                            pos.side = "Sell";
                            Console.WriteLine("Position is 0 in ModOrder.");
                            GetPositions(pos);
                        }
                        isOrderModified = true;
                    }
                    else if (result.ret_code != 0)
                    {
                        Console.WriteLine("Fatal error " + result.ret_code + " @ ModifyOrder()! Retrying operation! " + result.ret_msg);
                        ClosePositionQuick();
                    }
                    else
                    {
                        CurrentModOrder.price = bestAsk;
                        numReqs = result.rate_limit_status;
                        Task.Delay(2).Wait();
                        isOrderModified = true;
                    }
                }
                else
                {
                    Console.WriteLine("No response!");
                    ClosePositionQuick();
                }
            }
        }
        public void CancelAllOrders(string symbol = "ETHUSDT")
        {
            bool areOrdersCancelled = false;
            while (!areOrdersCancelled)
            {
                var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                string expires;
                string param_str = $"api_key={APIKey}&symbol={symbol}&timestamp={CurrTime}";

                Dictionary<string, object> param = new Dictionary<string, object>
                {
                    ["api_key"] = APIKey,
                    ["symbol"] = symbol,
                    ["timestamp"] = CurrTime
                };
                expires = CreateSignature(Secret, param_str);
                param.Add("sign", expires);
                var json = JsonConvert.SerializeObject(param);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{URL}{CancelAllOrdersStr}");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using var client = new HttpClient();
                HttpResponseMessage httpRes = client.SendAsync(request).Result;
                if (httpRes.Content != null)
                {
                    APIResponse result = JsonConvert.DeserializeObject<APIResponse>(httpRes.Content.ReadAsStringAsync().Result);
                    if (result.ret_code == 10006)
                    {
                        Console.WriteLine("Delaying until next reset");
                        Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                    }
                    else if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                    {
                        Console.WriteLine("Fatal error " + result.ret_code + " @ CancelAllOrders() ! Retrying operation!");
                        ClosePositionQuick();
                    }
                    else
                    {
                        Console.WriteLine("Orders Cancelled!");
                        CurrentModOrder = null;
                        CurrentCloseOrder = null;
                        areOrdersCancelled = true;
                    }
                }
                else
                {
                    Console.WriteLine("No response!");
                    ClosePositionQuick();
                }
            }
            beginSlPrice = 0;
        }
        public void CancelOrder(string orderID, string symbol = "ETHUSDT")
        {
            bool areOrdersCancelled = false;
            while (!areOrdersCancelled)
            {
                var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                string expires;
                string param_str = $"api_key={APIKey}&order_id={orderID}&symbol={symbol}&timestamp={CurrTime}";
                expires = CreateSignature(Secret, param_str);
                param_str += $"&sign={expires}";
                var val = param_str.Split("&");
                var json = "{" +
                    string.Join(",",
                    val.Select(val => val.Split('=')).Select(s => string.Format("\"{0}\": \"{1}\"", s[0], s[1]))) +
                "}";
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var httpRes = client.PostAsync(URL + CancelAllOrdersStr, httpContent).Result;
                if (httpRes.Content != null)
                {
                    APIResponse result = JsonConvert.DeserializeObject<APIResponse>(httpRes.Content.ReadAsStringAsync().Result);
                    if (result.ret_code == 10006)
                    {
                        Console.WriteLine("Delaying until next reset");
                        Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                    }
                    else if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                    {
                        Console.WriteLine("Fatal error " + result.ext_code + " @ CancelOrder() ! Retrying operation!");
                    }
                    else
                    {
                        Console.WriteLine("Order Cancelled!");
                        CurrentCloseOrder = null;
                        CurrentPosFilled = true;
                        areOrdersCancelled = true;
                    }
                }
                else
                {
                    Console.WriteLine("No response!");
                    ClosePositionQuick();
                }
            }
        }
        public bool PingAWS(string IP)
        {

            HttpResponseMessage res = AWSClient.GetAsync("http://" + IP + "/latest/meta-data/spot/instance-action").Result;
            if (res.StatusCode != HttpStatusCode.NotFound)
            {
                CancelAllOrders();
                if (CurrentPositionSize != 0)
                {
                    ClosePositionQuick();
                }

                return true;
            }
            return false;
        }
        public void GetPong(ResponseMessage x)
        {
            Pong p = JsonConvert.DeserializeObject<Pong>(x.Text);
            if (!p.success)
            {
                Console.WriteLine("Ping failed!" + p.ret_msg);
                ClosePositionQuick();
            }
        }
        public void GetWalletBalance()
        {
            bool BalanceRecieved = false;
            if (CurrBalance != 0)
            {
                Console.WriteLine("Account balance was given. Returning");
                return;
            }
            int t = 0;
            while (!BalanceRecieved)
            {
                if (t > 6)
                {
                    Console.WriteLine("Failed to get wallet balance!");
                    ClosePositionQuick();
                }
                var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                string expires;
                string param_str = $"api_key={APIKey}&coin=USDT&timestamp={CurrTime}";
                expires = CreateSignature(Secret, param_str);
                param_str += $"&sign={expires}";
                var httpRes = client.GetAsync(URL + WalletStr + "?" + param_str).Result;
                if (httpRes.IsSuccessStatusCode)
                {
                    APIResponse result = JsonConvert.DeserializeObject<APIResponse>(httpRes.Content.ReadAsStringAsync().Result);
                    if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                    {
                        Console.WriteLine("Fatal error " + result.ret_code + " @ GetWalletBal() ! Retrying operation!" + result.ret_msg);
                        ClosePositionQuick();
                    }
                    else
                    {
                        JToken json = JsonConvert.DeserializeObject<JToken>(httpRes.Content.ReadAsStringAsync().Result);
                        CurrBalance = double.Parse(json["result"]["USDT"]["equity"].ToString());
                        StartAccBalance = CurrBalance;
                        Console.WriteLine("Wallet Balance: " + CurrBalance);
                        BalanceRecieved = true;
                    }
                }
                else
                {
                    Console.WriteLine("No response!");
                }
                t++;
            }
        }
        public void Reconnect(WebsocketClient cl, string submsg, bool isPriv = false)
        {
            Console.WriteLine("Lost connection to " + cl.Url.ToString() + ", reconnecting!");
            if (isPriv)
            {
                cl.SendInstant(Auth(APIKey, Secret)).Wait();
                cl.SendInstant(submsg).Wait();
                if (CheckPosSize() <= 0)
                {
                    Position pos = new Position();
                    pos.entry_price = CurrentPositionEntry;
                    pos.size = 0;
                    GetPositions(pos);
                }
            }
            else
            {
                cl.SendInstant(submsg).Wait();
            }
        }
        public void ClosePosition()
        {
            if (CurrentModOrder != null)
            {
                if (CurrentModOrder.price < LastAsk - 0.05)
                {
                    ModifyOrder();
                }
                else
                {
                    return;
                }
            }
            else
            {
                Console.WriteLine("No Mod Order, creating new one!");
                CreateNewOrder("Buy", isClosing: true, reduceOnly: true).Wait();
            }
        }
        public void GetPositions(Position pos)
        {
            if (closingPos && pos.size > 0)
            {
                // CurrentPositionSize = pos.size;
                return;
            }

            else if (pos.size == 0 && closingPos)
            {
                checkModOrder.Stop();
                GetPrevTrades();
            }
            else if (pos.size == 0 && CurrentPositionSize != 0)
            {
                checkModOrder.Stop();
                GetPrevTrades();
            }
            else if (!CurrentPosFilled && CurrentCloseOrder != null)
            {
                CurrentPositionSize = pos.size;
                CurrentPositionEntry = pos.entry_price;
                if (CurrentPositionSize >= CurrentCloseOrder.qty)
                {
                    CurrentPosFilled = true;
                }
            }
            /*else if (pos.size < CurrentPositionSize && CurrentCloseOrder == null)
            {
                CurrentPositionSize = pos.size;
                Console.WriteLine("Position partially closed!");
            }*/
            /*if(CurrentCloseOrder == null || CurrentPositionSize < CurrentCloseOrder.qty)
        {
            CurrentPositionSize += pos.size;
            CurrentPositionEntry = pos.entry_price;
            Console.WriteLine("Current Position size: " + CurrentPositionSize);
            CreateCancelTPOrder(CurrentPositionSize).Wait();
        }*/
        }
        public async Task CheckOrders(double Amount)
        {
            await Task.Delay(90000);
            if (!CurrentPosFilled)
            {
                if (CurrentPositionSize == 0)
                {
                    CancelAllOrders();
                }
                else if (CurrentPositionSize != Amount)
                {
                    CancelOrder(CurrentCloseOrder.order_id);
                }
            }
        }
        public double CheckPosSize()
        {
            bool PosSizeChecked = false;
            int t = 0;
            double posSize = 0;
            while (!PosSizeChecked)
            {
                if (t > 6)
                {
                    Console.WriteLine("Failed to get Pos Size!");
                    ClosePositionQuick();
                }
                var CurrTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
                string expires;
                string param_str = $"api_key={APIKey}&symbol=ETHUSDT&timestamp={CurrTime}";
                expires = CreateSignature(Secret, param_str);
                param_str += $"&sign={expires}";

                var httpRes = client.GetAsync(URL + GetPosStr + "?" + param_str).Result;
                if (httpRes.IsSuccessStatusCode)
                {
                    APIResponse result = JsonConvert.DeserializeObject<APIResponse>(httpRes.Content.ReadAsStringAsync().Result);
                    if (result.ret_code == 10006)
                    {
                        Console.WriteLine("Delaying until next reset");
                        Task.Delay(Convert.ToInt32(result.rate_limit_reset_ms + 20 - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Wait();
                    }
                    else if (result.ret_code != 0 && !NormalError.Contains(result.ret_code))
                    {
                        Console.WriteLine("Fatal error " + result.ret_code + " @ CheckPosSize() ! Retrying operation!" + result.ret_msg);
                        ClosePositionQuick();
                    }
                    else
                    {
                        JToken j = JsonConvert.DeserializeObject<JToken>(httpRes.Content.ReadAsStringAsync().Result);
                        List<Position> CurrentPos = j["result"].ToObject<List<Position>>();
                        posSize = CurrentPos.Where(x => x.side == "Sell").First().size;
                        PosSizeChecked = true;
                    }
                }
                else
                {
                    Console.WriteLine("No response!");
                }
                t++;
            }
            return posSize;
        }
        public void PrivateEndpoints(ResponseMessage x, long TimeLeft)
        {

            JObject json = JsonConvert.DeserializeObject<JObject>(x.Text);
            if (json.ContainsKey("success"))
            {
                if (Boolean.Parse(json["success"].ToString()) == true)
                {
                    Console.WriteLine("Success was acknowledged for private socket");
                }
                else
                {
                    Console.WriteLine("Failure noted. " + json["ret_msg"].ToString());
                    ClosePositionQuick();
                }
            }
            else
            {
                var PosL = json["data"][0].ToObject<Position>();
                GetPositions(PosL);
            }
        }
        public string CreateSignature(string secret, string message)
        {
            var signatureBytes = Hmacsha256(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message));

            return ByteArrayToString(signatureBytes);
        }
        public byte[] Hmacsha256(byte[] keyByte, byte[] messageBytes)
        {
            using (var hash = new HMACSHA256(keyByte))
            {
                return hash.ComputeHash(messageBytes);
            }
        }
        public string ByteArrayToString(byte[] ba)
        {
            var hex = new StringBuilder(ba.Length * 2);

            foreach (var b in ba)
            {
                hex.AppendFormat("{0:x2}", b);
            }
            return hex.ToString();
        }
        public void CheckForErrors(ResponseMessage x)
        {
            ConnectionErrorCheck c = JsonConvert.DeserializeObject<ConnectionErrorCheck>(x.Text);
            if (!c.success)
            {
                Console.WriteLine("Connection failed. " + c.ret_msg);
                ClosePositionQuick();
            }
        }
        public async Task newResponseGateio(ResponseMessage x, List<Trade> trades)
        {
            if (x.Text.Contains("success"))
            {
                return;
            }
            ParsedTradeResult t = JsonConvert.DeserializeObject<ParsedTradeResult>(x.Text);
            if (t.channel == "futures.trades")
            {
                await newTrade(trades, t);
            }
        }
        public void CreateCandle(List<Candle> candles, List<Trade> t)
        {
            if (t == null || t.Count == 0)
                return;

            Candle cdl = new Candle();
            Candle prevCdl;

            if (candles.Count != 0)
            {
                prevCdl = candles.Last();
            }
            else
            {
                var O = t.First().price;
                prevCdl = new Candle();
                prevCdl.Open = O;
                prevCdl.Close = O;
            }
            var C = t.Last().price;
            var H = t.Max(t => t.price);
            var L = t.Min(t => t.price);
            cdl.Open = Math.Round((prevCdl.Open + prevCdl.Close) / 2, 3);
            cdl.Close = Math.Round((cdl.Open + C + H + L) / 4, 3);
            cdl.High = Math.Round(H, 1);
            cdl.Low = Math.Round(L, 1);
            cdl.TR = (candles.Count >= 1) ? new[] { cdl.High - cdl.Low, Math.Abs(cdl.High - prevCdl.Close), Math.Abs(cdl.Low - prevCdl.Close) }.Max() / 14 : 0;

            if (candles.Count >= 15)
            {
                double ATR = 0;
                double upbars = 0;
                double downBars = 0;
                if (cdl.Open >= cdl.Close)
                    downBars++;
                else
                    upbars++;
                for (int i = 1; i < 15; i++)
                {
                    if (i < 10)
                    {
                        if (candles[candles.Count - i].Open >= candles[candles.Count - i].Close)
                            downBars++;
                        else
                            upbars++;
                    }
                    ATR += candles[candles.Count - i].TR;
                }
                cdl.AdvDecVal = Math.Round((downBars == 0 ? upbars : upbars / downBars), 2);
                cdl.ATR = Math.Round(ATR, 3);
            }
            candles.Add(cdl);
            last = t.Last();
            t.Clear();
            // Console.WriteLine("Candle created. Num Candles: " + candles.Count + ", Open: " + cdl.Open + ", Close: " + cdl.Close + " Adv/Dec: " + cdl.AdvDecVal);

            if (candles.Count >= 50)
            {
                candles.RemoveRange(0, candles.Count / 2 - 1);
            }
        }
        public string Auth(string apikey, string apisecret)
        {
            var expires = DateTimeOffset.UtcNow.AddSeconds(1).ToUnixTimeSeconds() * 1000;
            var signature = CreateSignature(Secret, $"GET/realtime{expires}");
            return JsonConvert.SerializeObject(new { op = "auth", args = new object[] { APIKey, expires, signature } });

        }
        public async Task newTrade(List<Trade> trades, ParsedTradeResult currTrade)
        {
            Trade currT = currTrade.result[0];

            if (trades.Count != 0)
            {
                if (currT.create_time_ms != trades.Last().create_time_ms || (currT.create_time_ms == trades.Last().create_time_ms && currT.size != trades.Last().size))
                {
                    trades.Add(currT);
                }

            }
            else
            {
                trades.Add(currT);
            }
        }
        public async Task setOrderbook(ParsedResult baseData, Dictionary<double, string> OBk, ResponseMessage x)
        {
            if (x.Text.Contains("success"))
            {
                Console.WriteLine("Success!");
                return;
            }
            ParsedResult book = JsonConvert.DeserializeObject<ParsedResult>(x.Text);
            if (book.topic == "orderBookL2_25.ETHUSDT")
            {
                if (OBk.Count == 0 || book.timestamp_e6 != baseData.timestamp_e6)
                {
                    var bookData = book.data;
                    if (book.type == "snapshot")
                    {
                        foreach (BookOrder o in bookData.order_book)
                        {
                            OBk.Add(o.price, o.side);
                        }
                        Console.WriteLine("Orderbook Created");
                    }
                    else
                    {
                        for (int i = 0; i < new[] { bookData.insert.Length, bookData.update.Length, bookData.delete.Length }.Max(); i++)
                        {

                            if (i < bookData.delete.Length)
                            {
                                OBk.Remove(bookData.delete[i].price);
                            }
                            if (i < bookData.update.Length)
                            {
                                OBk[bookData.update[i].price] = bookData.update[i].side;
                            }
                            if (i < bookData.insert.Length)
                            {
                                if (!OBk.ContainsKey(bookData.insert[i].price))
                                    OBk.Add(bookData.insert[i].price, bookData.insert[i].side);
                                else
                                {
                                    OBk[bookData.insert[i].price] = bookData.insert[i].side;
                                }
                            }
                        }
                    }
                    baseData = book;

                    LastAsk = OBk.Where(x => x.Value == "Sell").OrderBy(x => x.Key).FirstOrDefault().Key;
                    if (OBk.Count > 50)
                    {
                        Console.WriteLine("Orderbook size: " + OBk.Count);
                    }
                    if (closingPos)
                    {
                        ClosePosition();
                    }
                }
            }
        }
    }
}
