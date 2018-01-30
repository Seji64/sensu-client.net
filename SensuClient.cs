using System;
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

        private static IConnection m_rabbitmq_connection;
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private const int KeepAliveTimeout = 20000;
        private static readonly CancellationTokenSource m_cancelationtokensrc = new CancellationTokenSource();
        private static JObject _configsettings;
        private static bool _safemode;
        private static readonly List<string> ChecksInProgress = new List<string>();
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings { Formatting = Formatting.None };

        #region "Core"

        public static void Start()
        {
            LoadConfiguration();

            Connect2RabbitMQ();

            if (m_rabbitmq_connection != null && m_rabbitmq_connection.IsOpen)
            {

                //Start Keepalive thread
                System.Threading.Tasks.Task.Run(() => KeepAliveScheduler(m_cancelationtokensrc.Token), m_cancelationtokensrc.Token);

                //Start subscriptions thread.
                System.Threading.Tasks.Task.Run(() => Subscribe(m_cancelationtokensrc.Token), m_cancelationtokensrc.Token);

            }
            else
            {
                Halt();
            }

        }

        private const string Configfilename = "config.json";
        private const string Configdirname = "conf.d";

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

        private static void KeepAliveScheduler(CancellationToken m_token)
        {

            IModel m_rabbitmq_channel = null;

            Log.Debug("Starting keepalive scheduler thread");

            try
            {

                while (!m_token.IsCancellationRequested)
                {

                    if (m_rabbitmq_connection != null && m_rabbitmq_connection.IsOpen)
                    {
                        m_rabbitmq_channel = m_rabbitmq_connection.CreateModel();

                        if (m_rabbitmq_channel == null || !m_rabbitmq_channel.IsOpen)
                        {
                            Log.Error("RabbitMQ Channel is NOT Ready....waiting for reconnect");
                        }
                        else
                        {

                            try
                            {

                                var payload = _configsettings["client"];
                                payload["timestamp"] = Convert.ToInt64(Math.Round((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds, MidpointRounding.AwayFromZero));

                                Log.Debug("Publishing keepalive");

                                var properties = new BasicProperties
                                {
                                    ContentType = "application/octet-stream",
                                    Priority = 0,
                                    DeliveryMode = 1
                                };

                                m_rabbitmq_channel.BasicPublish("", "keepalives", properties, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));

                            }
                            catch (Exception)
                            {
                                Log.Error("Failed to publish keepalive!");
                            }
                            finally
                            {
                                System.Threading.Tasks.Task.Delay(KeepAliveTimeout, m_token).Wait();
                            }

                        }

                    }
                    else
                    {
                        Log.Error("RabbitMQ Connection is NOT Ready....waiting for reconnect");
                    }

                }

            }
            catch (OperationCanceledException)
            {
                m_rabbitmq_channel.Close(200, "Bye");
                Log.Info("Keepalive stopped");

            }
            finally
            {
                System.Threading.Tasks.Task.Delay(KeepAliveTimeout, m_token).Wait();
            }
        }

        private static void Subscribe(CancellationToken m_token)
        {

            IModel m_rabbitmq_channel = null;
            EventingBasicConsumer m_rabbit_consumer = null;

            m_rabbitmq_channel = m_rabbitmq_connection.CreateModel();

            var m_my_queue = m_rabbitmq_channel.QueueDeclare("", false, false, true, null);

            m_rabbitmq_channel.BasicQos(0, 1, false);

            foreach (var subscription in _configsettings["client"]["subscriptions"])
            {

                Log.Debug("Binding queue {0} to exchange {1}", m_my_queue.QueueName, subscription);
                m_rabbitmq_channel.QueueBind(m_my_queue.QueueName, subscription.ToString(), "");
                m_rabbit_consumer = new EventingBasicConsumer(m_rabbitmq_channel);

                m_rabbit_consumer.Received += SubscriptionReceived;
                m_rabbit_consumer.Shutdown += SubscriptionShutdown;
                m_rabbit_consumer.ConsumerCancelled += SubscriptionCancelled;

                m_rabbitmq_channel.BasicConsume(m_my_queue.QueueName, true, m_rabbit_consumer);

            }

        }

        public static void ProcessCheck(JObject check)
        {

            JToken command;

            Log.Debug("Processing check {0}", JsonConvert.SerializeObject(check, SerializerSettings));

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
            using (IModel m_rabbitmq_channel = m_rabbitmq_connection.CreateModel())
            {
                var properties = new BasicProperties
                {
                    ContentType = "application/octet-stream",
                    Priority = 0,
                    DeliveryMode = 1
                };
                m_rabbitmq_channel.BasicPublish("", "results", properties, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
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
            m_rabbitmq_connection.AutoClose = true;
            m_cancelationtokensrc.Cancel();
            m_rabbitmq_connection.Close();
            base.OnStop();
        }


        private static void Connect2RabbitMQ()
        {

            try
            {

                var connectionFactory = new ConnectionFactory
                {
                    HostName = _configsettings["rabbitmq"]["host"].ToString(),
                    Port = int.Parse(_configsettings["rabbitmq"]["port"].ToString()),
                    UserName = _configsettings["rabbitmq"]["user"].ToString(),
                    Password = _configsettings["rabbitmq"]["password"].ToString(),
                    VirtualHost = _configsettings["rabbitmq"]["vhost"].ToString(),
                    TopologyRecoveryEnabled = true,
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(30),
                    RequestedHeartbeat = 30
                };

                m_rabbitmq_connection = connectionFactory.CreateConnection();
                m_rabbitmq_connection.ConnectionShutdown += RabbitMQConnection_Shutdown;
                m_rabbitmq_connection.RecoverySucceeded += RabbitMQConnecion_ReconnectSuccess;
                m_rabbitmq_connection.ConnectionRecoveryError += RabbitMQConnect_ReconnectFailed;


                Log.Debug("Connection successfully created!");

            }
            catch (ConnectFailureException ex)
            {
                Log.Error(ex, "unable to open rMQ connection");
            }
            catch (BrokerUnreachableException ex)
            {
                Log.Error(ex, "rMQ endpoint unreachable");
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
        }


        #endregion

        #region "RabbitMQ Events"

        private static void SubscriptionReceived(object sender, BasicDeliverEventArgs e)
        {
            var m_payload = String.Empty;

            try
            {

                Log.Debug("Received check request");

                if (e.Body != null)
                {

                    m_payload = Encoding.UTF8.GetString(e.Body);
                    var m_check = JObject.Parse(m_payload);

                    Log.Debug("Payload Data: {0}", JsonConvert.SerializeObject(m_check, SerializerSettings));

                    ProcessCheck(m_check);

                }
                else
                {
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

        private static void SubscriptionShutdown(object sender, ShutdownEventArgs e)
        {
            Log.Error("SubscriptionShutdown: {0}", e.ToString());
        }

        private static void SubscriptionCancelled(object sender, ConsumerEventArgs e)
        {
            Log.Error("SubscriptionCancelled: {0}", e.ToString());
        }

        private static void RabbitMQConnect_ReconnectFailed(object sender, ConnectionRecoveryErrorEventArgs e)
        {
            Log.Error("Reconnect to RabbitMQ failed: {0}", e.Exception.Message.ToString());
        }

        private static void RabbitMQConnecion_ReconnectSuccess(object sender, EventArgs e)
        {
            Log.Info("Reconnect to RabbitMQ successfull!");
        }

        private static void RabbitMQConnection_Shutdown(object sender, ShutdownEventArgs e)
        {
            if (e.Initiator == ShutdownInitiator.Peer)
            {
                Log.Warn("Connection Shutdown initiaded by Peer");
            }
            else
            {
                Log.Error("RabbitMQ Connection exited unexpected!");
            }

            if (e.Initiator == ShutdownInitiator.Application)
            {
                Log.Warn("Connection Shutdown initiaded by Application");
            }
            else
            {
                Log.Error("RabbitMQ Connection exited unexpected!");
            }

            if (e.Initiator == ShutdownInitiator.Library)
            {
                Log.Warn("Connection Shutdown initiaded by Lib");
            }
            else
            {
                Log.Error("RabbitMQ Connection exited unexpected!");
            }
        }

        #endregion




    }
}
