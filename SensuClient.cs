﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Client.Framing;
using System.Threading.Tasks;

namespace sensu_client.net
{
    public class SensuClient : ServiceBase
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private const int KeepAliveTimeout = 20000;
        private static readonly object MonitorObject = new object();
        private static bool _quitloop;
        private static JObject _configsettings;
        private const string Configfilename = "config.json";
        private const string Configdirname = "conf.d";
        private static bool _safemode;
        private static readonly List<string> ChecksInProgress = new List<string>();
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings { Formatting = Formatting.None };

        public static void Start()
        {
            LoadConfiguration();
       
            //Start Keepalive thread
            var keepalivethread = new Thread(KeepAliveScheduler);
            keepalivethread.Start();

            //Start subscriptions thread.
            Subscribe();
        }

        public static void LoadConfiguration()
        {
            var configfile = string.Concat(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Path.DirectorySeparatorChar,
                Configfilename);
            var configdir = string.Concat(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                Path.DirectorySeparatorChar,
                Configdirname);
            //Read Config settings
            try
            {
                _configsettings = JObject.Parse(File.ReadAllText(configfile));
            }
            catch (FileNotFoundException ex)
            {
                Log.Error(ex, string.Format("Config file not found: {0}", configfile));
                _configsettings = new JObject();
            }
            //Grab configs from dir.
            if (Directory.Exists(configdir))
            {
                foreach (
                    var settings in
                        Directory.EnumerateFiles(configdir).Select(file => JObject.Parse(File.ReadAllText(file))))
                {
                    foreach (var thingemebob in settings)
                    {
                        _configsettings.Add(thingemebob.Key, thingemebob.Value);
                    }
                }
                try
                {
                    bool.TryParse(_configsettings["client"]["safemode"].ToString(), out _safemode);
                }
                catch (NullReferenceException)
                {
                }
            }
            else
            {
                Log.Warn("Config dir not found");
            }
        }

        private static void KeepAliveScheduler()
        {
            IModel ch = null;
            Log.Debug("Starting keepalive scheduler thread");
            while (true)
            {
                if (ch == null || !ch.IsOpen)
                {
                    Log.Error("rMQ Q is closed, Getting connection");
                    var connection = GetRabbitConnection();
                    if (connection == null)
                    {
                        //Do nothing - we'll loop around the while loop again with everything null and retry the connection.
                    }
                    else
                    {
                        ch = connection.CreateModel();
                    }
                }
                if (ch != null && ch.IsOpen)
                {
                    //Valid channel. Good to publish.
                    var payload = _configsettings["client"];
                    payload["timestamp"] =
                        Convert.ToInt64(
                            Math.Round((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds,
                                       MidpointRounding.AwayFromZero));
                    Log.Debug("Publishing keepalive");
                    var properties = new BasicProperties
                        {
                            ContentType = "application/octet-stream",
                            Priority = 0,
                            DeliveryMode = 1
                        };
                    ch.BasicPublish("", "keepalives", properties,
                                    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
                }
                else
                {
                    Log.Error("Valiant attempts to get a valid rMQ connection were in vain. Skipping this keepalive loop.");
                }

                //Lets us quit while we're still sleeping.
                lock (MonitorObject)
                {
                    if (_quitloop)
                    {
                        Log.Warn("Quitloop set, exiting main loop");
                        break;
                    }
                    Monitor.Wait(MonitorObject, KeepAliveTimeout);
                    if (_quitloop)
                    {
                        Log.Warn("Quitloop set, exiting main loop");
                        break;
                    }
                }
            }
        }

        private static void Subscribe()

        {

            IModel m_rabbit_channel = null;
            EventingBasicConsumer m_rabbit_consumer = null;

            Log.Debug("Subscribing to client subscriptions");

            Log.Warn("Creating rabbitMQ Connection");

            var m_rabbit_connection = GetRabbitConnection();

            m_rabbit_channel = m_rabbit_connection.CreateModel();

            var m_my_queue = m_rabbit_channel.QueueDeclare("", false, false, true, null);

            foreach (var subscription in _configsettings["client"]["subscriptions"])

            {

                Log.Debug("Binding queue {0} to exchange {1}", m_my_queue.QueueName, subscription);

                m_rabbit_channel.QueueBind(m_my_queue.QueueName, subscription.ToString(), "");
                m_rabbit_consumer = new EventingBasicConsumer(m_rabbit_channel);

                if (m_rabbit_channel != null && m_rabbit_channel.IsOpen)
                {
                    m_rabbit_consumer.Received += SubscriptionReceived;
                    m_rabbit_channel.BasicConsume(m_my_queue.QueueName, true, m_rabbit_consumer);
                }
                else
                {
                    //Failed to open
                }

            }

        }

        private static void SubscriptionReceived(object sender, BasicDeliverEventArgs e)
        {
            var m_payload = String.Empty;

            try

            {

                Log.Debug("Received check request");

                if (e.Body != null) {

                    m_payload = Encoding.UTF8.GetString(e.Body);
                    var m_check = JObject.Parse(m_payload);

                    Log.Debug("Payload Data: {0}", JsonConvert.SerializeObject(m_check, SerializerSettings));

                    ProcessCheck(m_check);

                } else {
                    throw new Exception("payload empty or null");
                }

            }

            catch (JsonReaderException json_r_ex)
            {
                Log.Error(json_r_ex, "Malformed Check request: {0}", m_payload);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "failed to process check request!");
            }

        }

        public static void ProcessCheck(JObject check)
        {
            Log.Debug("Processing check {0}", JsonConvert.SerializeObject(check, SerializerSettings));
            JToken command;
            if (check.TryGetValue("command", out command))
            {
                if (_configsettings["check"] != null && _configsettings["check"].Contains(check["name"]))
                {
                    foreach (var thingie in _configsettings["checks"][check["name"]])
                    {
                        check.Add(thingie);
                    }
                    ExecuteCheckCommand(check);
                }
                else if (_safemode)
                {
                    check["output"] = "Check is not locally defined (safemode)";
                    check["status"] = 3;
                    check["handle"] = false;
                    PublishResult(check);
                }
                else
                {
                    ExecuteCheckCommand(check);
                }
            }
            else
            {
                Log.Warn("Unknown check exception: {0}", check);
            }
        }

        private static void PublishResult(JObject check)
        {
            var payload = new JObject();
            payload["check"] = check;
            payload["client"] = _configsettings["client"]["name"];

            Log.Info("Publishing Check {0}", JsonConvert.SerializeObject(payload, SerializerSettings));
            using (var ch = GetRabbitConnection().CreateModel())
            {
                var properties = new BasicProperties
                {
                    ContentType = "application/octet-stream",
                    Priority = 0,
                    DeliveryMode = 1
                };
                ch.BasicPublish("", "results", properties, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
            }
            ChecksInProgress.Remove(check["name"].ToString());
        }

        public static void ExecuteCheckCommand(JObject check)
        {
            Log.Debug("Attempting to execute check command {0}", JsonConvert.SerializeObject(check, SerializerSettings));
            if (check["name"] == null)
            {
                check["output"] = "Check didn't have a valid name";
                check["status"] = 3;
                check["handle"] = false;
                PublishResult(check);
                return;
            }
            if (!ChecksInProgress.Contains(check["name"].ToString()))
            {
                ChecksInProgress.Add(check["name"].ToString());
                List<string> unmatchedTokens;
                var command = SubstitueCommandTokens(check, out unmatchedTokens);
                command = command.Trim();
                if (unmatchedTokens == null || unmatchedTokens.Count == 0)
                {
                    var checkCommand = "";
                    var checkArgs = "";
                    if (command.Contains(" "))
                    {
                        var parts = command.Split(" ".ToCharArray(), 2);
                        checkCommand = parts[0];
                        checkArgs = parts[1];
                    }
                    else
                    {
                        checkCommand = command;
                    }
                   
                    var processstartinfo = new ProcessStartInfo(checkCommand)
                        {
                            WindowStyle = ProcessWindowStyle.Hidden,
                            UseShellExecute = false,
                            RedirectStandardError = true,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            Arguments = checkArgs
                        };
                    var process = new Process { StartInfo = processstartinfo };
                    var stopwatch = new Stopwatch();
                    try
                    {
                        stopwatch.Start();
                        process.Start();
                        if (check["timeout"] != null)
                        {
                            if (!process.WaitForExit(1000 * int.Parse(check["timeout"].ToString())))
                            {
                                process.Kill();
                            }
                        }
                        else
                        {
                            process.WaitForExit();
                        }

                        check["output"] = string.Format("{0}{1}", process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
                        check["status"] = process.ExitCode;
                    }
                    catch (Win32Exception ex)
                    {
                        check["output"] = string.Format("Unexpected error: {0}", ex.Message);
                        check["status"] = 2;
                    }
                    stopwatch.Stop();

                    check["duration"] = string.Format("{0:f3}", ((float)stopwatch.ElapsedMilliseconds) / 1000);
                    PublishResult(check);
                }
                else
                {
                    check["output"] = string.Format("Unmatched command tokens: {0}",
                                                    string.Join(",", unmatchedTokens.ToArray()));
                    check["status"] = 3;
                    check["handle"] = false;
                    PublishResult(check);
                    ChecksInProgress.Remove(check["name"].ToString());
                }
            }
            else
            {
                Log.Warn("Previous check command execution in progress {0}", check["command"]);
            }
        }

        private static string SubstitueCommandTokens(JObject check, out List<string> unmatchedTokens)
        {
            var temptokens = new List<string>();
            var command = check["command"].ToString();
            var blah = new Regex(":::(.*?):::", RegexOptions.Compiled);
            command = blah.Replace(command, match =>
                {
                    var matched = "";
                    foreach (var p in match.Value.Split('.'))
                    {
                        if (_configsettings["client"][p] != null)
                        {
                            matched += _configsettings["client"][p];
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (string.IsNullOrEmpty(matched)) { temptokens.Add(match.Value); }
                    return matched;
                });
            unmatchedTokens = temptokens;
            return command;
        }


        public static void Halt()
        {
            Log.Info("Told to stop. Obeying.");
            Environment.Exit(1);
        }
        protected override void OnStart(string[] args)
        {
            Start();
            base.OnStart(args);
        }

        protected override void OnStop()
        {
            Log.Info("Service OnStop called: Shutting Down");
            Log.Info("Attempting to obtain lock on monitor");
            lock (MonitorObject)
            {
                Log.Info("lock obtained");
                _quitloop = true;
                Monitor.Pulse(MonitorObject);
            }
            base.OnStop();
        }
        private static IConnection _rabbitMqConnection;
        private static readonly object Connectionlock = new object();
        private static IConnection GetRabbitConnection()
        {
            //One at a time, please
            lock (Connectionlock)
            {
                if (_rabbitMqConnection == null || !_rabbitMqConnection.IsOpen)
                {
                    Log.Debug("No open rMQ connection available. Creating new one.");

                    if (_configsettings["rabbitmq"] == null)
                    {
                        Log.Error("rabbitmq not configured");
                        return null;
                    }

                    var connectionFactory = new ConnectionFactory
                        {
                            HostName = _configsettings["rabbitmq"]["host"].ToString(),
                            Port = int.Parse(_configsettings["rabbitmq"]["port"].ToString()),
                            UserName = _configsettings["rabbitmq"]["user"].ToString(),
                            Password = _configsettings["rabbitmq"]["password"].ToString(),
                            VirtualHost = _configsettings["rabbitmq"]["vhost"].ToString()
                        };
                    try
                    {
                        _rabbitMqConnection = connectionFactory.CreateConnection();
                    }
                    catch (ConnectFailureException ex)
                    {
                        Log.Error(ex, "unable to open rMQ connection");
                        return null;
                    }
                    catch (BrokerUnreachableException ex)
                    {
                        Log.Error(ex, "rMQ endpoint unreachable");
                        return null;
                    }
                }
            }
            return _rabbitMqConnection;
        }
    }
}
