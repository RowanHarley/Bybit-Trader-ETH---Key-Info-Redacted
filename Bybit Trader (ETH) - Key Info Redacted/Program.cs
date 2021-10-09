using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Websocket.Client;

namespace Bybit_Trader__ETH_
{
    public class Bybit_Trader__ETH_
    {
        public static void Main(string[] args)
        {
            Bybit_Trader__ETH_ bt = new Bybit_Trader__ETH_();
            AlgoFunctions aF = new AlgoFunctions();
            #region Classes
            var Websocket = new Uri("wss://stream.bytick.com/realtime_public");
            var WebsocketPriv = new Uri("wss://stream.bytick.com/realtime_private"); //?api_key={api_key}&expires={expires}&signature={signature}
            var GateioW = new Uri("wss://fx-ws.gateio.ws/v4/ws/usdt");
            var publicData = new WebsocketClient(Websocket);
            var GatioData = new WebsocketClient(GateioW);
            DateTime lastAWSPing = new DateTime();
            var privWS = new WebsocketClient(WebsocketPriv);
            var BybitPubPing = new WebsocketClient(Websocket);
            var BybitPrivPing = new WebsocketClient(WebsocketPriv);
            bool isClosing = false;
            Trade last = new Trade();
            Position pos = new Position();
            WalletResult Bal = new WalletResult();
            #endregion
            #region Variables
            string sendTrades = "{\"time\" : " + DateTimeOffset.FromUnixTimeSeconds(DateTime.UtcNow.Second).Second + ", \"channel\" : \"futures.trades\", \"event\": \"subscribe\", \"payload\" : [\"ETH_USDT\"]}";
            string getOrderbook = "{\"op\": \"subscribe\", \"args\": [\"orderBookL2_25.ETHUSDT\"]}";
            string getPos = "{\"op\": \"subscribe\", \"args\": [\"position\"]}";
            long timeLeft = 0;
            HistTrade histTrade = new HistTrade();
            bool isLoaded = false;
            #endregion
            #region Lists
            List<Trade> trades = new List<Trade>();
            ParsedResult baseBook = new ParsedResult();
            List<Order> orders = new List<Order>();
            Dictionary<double, string> orderbook = new Dictionary<double, string>();
            List<Candle> candles = new List<Candle>();
            #endregion
            Console.WriteLine("Beginning Operation");
            aF.fundingTime = new DateTime(2021, 10, 10, 0, 0, 0);//new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, (DateTime.UtcNow.Hour >= 8 ? (DateTime.UtcNow.Hour >= 16 ? 23 : 15) : 7), 50, 0);
            aF.CheckFundingRate();
            System.Timers.Timer checkFunding = new System.Timers.Timer() { Interval = aF.fundingTime.AddMinutes(-10).Subtract(DateTime.UtcNow).TotalMilliseconds, AutoReset = false };
            checkFunding.Elapsed += (sender, e) =>
            {
                if (aF.CurrentPositionSize > 0 && !aF.CheckFundingRate())
                {
                    aF.closingPos = true;
                }
                checkFunding.Interval = 8 * 60 * 60 * 1000;
                aF.fundingTime.AddHours(8);
                checkFunding.Start();
            };
            if (!checkFunding.Enabled)
                checkFunding.Start();

            /*int i = 0;
            while (true)
            {
                if(i > 20)
                {
                    return;
                }
                if (i % 2 == 0)
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create("api.bytick.com");
                    System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                    timer.Start();

                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                    timer.Stop();

                    TimeSpan timeTaken = timer.Elapsed;
                    if (timeTaken.TotalMilliseconds < 0.5)
                    {
                        aF.URL = "api.bybit.com";
                        return;
                    }
                    else
                    {
                        i++;
                        Console.WriteLine("Server Response time to bybit: " + timeTaken.TotalMilliseconds);
                    }
                } 
                else
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create("api.bytick.com");
                    System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                    timer.Start();

                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                    timer.Stop();

                    TimeSpan timeTaken = timer.Elapsed;
                    if (timeTaken.TotalMilliseconds < 0.5)
                    {
                        aF.URL = "api.bybit.com";
                        return;
                    }
                        
                    else
                    {
                        i++;
                        Console.WriteLine("Server Response time to bytick: " + timeTaken.TotalMilliseconds);
                    }
                }
            }*/
            aF.GetWalletBalance();
            aF.CheckFundingRate();
            System.Timers.Timer sendMetrics = new System.Timers.Timer() { Interval = 180000 };
            System.Timers.Timer ping = new System.Timers.Timer() { Interval = 30000 };
            while (true)
            {

                sendMetrics.Elapsed += (sender, e) => aF.SendMetrics(candles);
                if (!sendMetrics.Enabled)
                    sendMetrics.Start();

                if (aF.LowMovementFinish >= DateTime.UtcNow)
                {
                    Console.WriteLine("Sleeping for " + aF.LowMovementFinish.Subtract(DateTime.UtcNow).TotalHours + " hours.");
                    Task.Delay(Convert.ToInt32(aF.LowMovementFinish.Subtract(DateTime.UtcNow).TotalMilliseconds)).Wait();
                    candles = null;
                    candles = new List<Candle>();
                    trades = null;
                    trades = new List<Trade>();
                    orderbook = null;
                    orderbook = new Dictionary<double, string>();
                    aF.CircuitBreakerHit = false;
                }
                else if (isClosing)
                {
                    Console.WriteLine("AWS Systems being stopped");
                    break;
                }
                aF.CreateBeginningCandles(candles, trades);
                if (!isLoaded)
                {
                    GatioData.StartOrFail().Wait();
                    privWS.StartOrFail().Wait();
                    BybitPubPing.StartOrFail().Wait();
                    publicData.StartOrFail().Wait();

                    aF.GetPrevTrades();
                    aF.checkModOrder.Elapsed += async (sender, e) => await aF.CheckModOrders();
                }
                Console.WriteLine("Connected");
                if (publicData.IsRunning)
                {
                    if (!isLoaded)
                    {
                        #region Private Websockets
                        privWS.MessageReceived.Where(x => !x.Text.Contains("pong")).Subscribe(x => aF.PrivateEndpoints(x, timeLeft));
                        privWS.SendInstant(aF.Auth(aF.APIKey, aF.Secret)).Wait();
                        privWS.SendInstant(getPos).Wait();
                        #endregion
                        privWS.MessageReceived.Where(x => x.Text.Contains("pong")).Subscribe(x => aF.GetPong(x));

                        Console.WriteLine("Authenticated with Wallet & Current network!");
                        publicData.SendInstant(getOrderbook).Wait();
                        GatioData.SendInstant(sendTrades).Wait();
                        GatioData.MessageReceived.Where(x => !x.Text.Contains("pong")).Subscribe(x => aF.newResponseGateio(x, trades).Wait());
                        publicData.MessageReceived.Where(x => !x.Text.Contains("success")).Subscribe(x => aF.setOrderbook(baseBook, orderbook, x).Wait());
                        publicData.DisconnectionHappened.Subscribe(x => aF.Reconnect(publicData, getOrderbook));
                        GatioData.DisconnectionHappened.Subscribe(x => aF.Reconnect(GatioData, sendTrades));
                        privWS.DisconnectionHappened.Subscribe(x => aF.Reconnect(privWS, getPos, true));

                        ping.Elapsed += (sender, e) => Task.Run(() =>
                        {
                            BybitPubPing.Send("{\"op\":\"ping\"}");
                            privWS.Send("{\"op\":\"ping\"}");
                            GatioData.Send("{\"time\" : " + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ", \"channel\" : \"futures.ping\"}");
                        });
                        #region Reconnect Timeouts
                        publicData.ReconnectTimeout = TimeSpan.FromSeconds(30);
                        GatioData.ReconnectTimeout = TimeSpan.FromSeconds(30);
                        GatioData.ReconnectionHappened.Subscribe(x => Console.WriteLine($"Reconnection happened on Gate.io Data. Type: {x.Type}"));
                        privWS.ReconnectTimeout = null;
                        #endregion
                    }

                    if (!ping.Enabled)
                        ping.Start();
                    isLoaded = true;
                    DateTime lastCandle = DateTime.UtcNow;
                    DateTime lastPing = DateTime.UtcNow;
                    while (true)
                    {
                        if (aF.CurrentPosFilled && aF.TPOrder == null && !aF.closingPos)
                        {
                            aF.CreateCancelTPOrder();
                        }
                        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % aF.TimeBetweenCandles == 0 && DateTime.UtcNow.AddSeconds(-2) > lastCandle)
                        {
                            aF.CreateCandle(candles, trades);
                            if (aF.CurrentCloseOrder == null && aF.CurrentPositionSize == 0 && candles[candles.Count - 2].AdvDecVal > 8 && candles.Last().AdvDecVal < candles[candles.Count - 2].AdvDecVal)
                            {
                                Console.WriteLine("Creating Order");
                                aF.CreateNewOrder("Sell", candles.Last());
                            }
                            lastCandle = DateTime.UtcNow;
                        }
                        if (aF.CurrentPosFilled && aF.LastAsk - 0.05 >= (aF.CurrentPositionEntry + 7 * aF.beginSlPrice / 8) && !aF.closingPos)
                        {
                            aF.ClosePosition(); // Closes position via moving limit orders to take advantage of 0.25% rebate
                        }
                        if (DateTimeOffset.FromUnixTimeSeconds(DateTime.UtcNow.Second).Second % 60 == 0 && DateTime.UtcNow.AddSeconds(-2) > lastAWSPing)
                        {
                            bool AWSClosing = aF.PingAWS("169.254.169.254");
                            lastAWSPing = DateTime.UtcNow;
                            if (AWSClosing)
                            {
                                Console.WriteLine("Closing due to AWS event");
                                Environment.Exit(0);
                            }
                        }
                        if (aF.CircuitBreakerHit)
                        {
                            Console.WriteLine("Circuit breaker hit, stopping application");
                            break;
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Bybit Connection failed");
                }
                sendMetrics.Stop();
                checkFunding.Stop();
                Console.WriteLine("Circuit broken @ " + DateTime.UtcNow);
            }

        }
    }
}
