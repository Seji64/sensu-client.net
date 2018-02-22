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
using sensu_client.net.pluginterface;
using sensu_client.net.Exceptions;
using System.Threading.Tasks;

namespace sensu_client.net
{
    public class SensuClient : ServiceBase
    {
        
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private const int KeepAliveTimeout = 20000;
        private static readonly CancellationTokenSource m_cancelationtokensrc = new CancellationTokenSource();
        private static JObject m_configsettings;
        private static bool _safemode;
        private static readonly List<string> ChecksInProgress = new List<string>();
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings { ContractResolver = new OrderedContractResolver(), Formatting = Formatting.None };
        private static Program m_program_base;
        private static object m_lock_checkinprogress;
        private static object m_lock_pulish;
        private static string m_version_string = String.Format("Sensu.NET {0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString());

        #region "Properties"

        private static IModel m_rabbitmq_channel = null;
        public static  IModel RabbitMQChannel
        {
            get
            {
                return m_rabbitmq_channel;
            }
        }

        private static IConnection m_rabbitmq_connection = null;
        public static IConnection RabbitMQConnection
        {
            get
            {
                return m_rabbitmq_connection;
            }
        }

        #endregion

        #region "Core"

        public static void Start()
        {

            m_program_base = new Program();

            m_lock_checkinprogress = new object();
            m_lock_pulish = new object();

            m_program_base.LoadPlugins();

            LoadConfiguration();

            Connect2RabbitMQ();

            if (RabbitMQConnection != null && RabbitMQConnection.IsOpen)
            {
                //CreateChannel
               if (CreateRabbitMQChannel())
                {
                    //Start Keepalive thread
                    System.Threading.Tasks.Task.Run(() => KeepAliveScheduler(m_cancelationtokensrc.Token), m_cancelationtokensrc.Token);

                    //Start subscriptions thread.
                    System.Threading.Tasks.Task.Run(() => Subscribe(m_cancelationtokensrc.Token), m_cancelationtokensrc.Token);
                }
                else
                {
                    Log.Error("Failed to open RabbitMQ Channel - stopping");
                    Halt();
                }
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
                m_configsettings = JObject.Parse(File.ReadAllText(configfile));
            }
            catch (FileNotFoundException ex)
            {
                Log.Error(ex, string.Format("Config file not found: {0}", configfile));
                m_configsettings = new JObject();
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
                        m_configsettings.Add(thingemebob.Key, thingemebob.Value);
                    }
                }
                try
                {
                    bool.TryParse(m_configsettings["client"]["safemode"].ToString(), out _safemode);
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

        private static bool CreateRabbitMQChannel()
        {

            bool m_sucess = true;

            Log.Info("Creating/Opening new RabbitMQ Channel...");

            try
            {

                m_rabbitmq_channel = RabbitMQConnection.CreateModel();

                if (!m_rabbitmq_channel.IsOpen)
                {
                    throw new Exception("Failed to open Channel");
                }

                m_rabbitmq_channel.BasicQos(0, 1, false);

                Log.Info("Done - RabbitMQ Channel successfully opened");

            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                m_sucess = false;
            }

            return m_sucess;

        }

        private static void KeepAliveScheduler(CancellationToken m_token)
        {

            Log.Info("Starting KeepAlive Scheduler");

            try
            {

                while (!m_token.IsCancellationRequested)
                {

                    if (RabbitMQConnection != null && RabbitMQConnection.IsOpen)
                    {                
                        if (!RabbitMQChannel.IsOpen || RabbitMQChannel.IsClosed)
                        {
                            Log.Error("RabbitMQ Channel already created but is NOT Ready....waiting for reconnect");
                        }
                        else
                        {
                            PublishKeepAlive();                           
                        }
                    }
                    else
                    {
                        Log.Error("RabbitMQ Connection is NOT Ready....waiting for reconnect");
                    }

                    System.Threading.Tasks.Task.Delay(KeepAliveTimeout, m_token).Wait();

                }

            }
            catch (OperationCanceledException)
            {                
                Log.Info("KeepAlive Scheduler stopped");

            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }
            finally
            {
                System.Threading.Tasks.Task.Delay(KeepAliveTimeout, m_token).Wait();
            }
        }

        private static void Subscribe(CancellationToken m_tokenl)
        {

            EventingBasicConsumer m_rabbit_consumer = null;

            if (!RabbitMQChannel.IsOpen)
            {
                throw new Exception("RabbitMQ Channel not open!");
            }

            try
            {

               

                var m_my_queue = RabbitMQChannel.QueueDeclare(SensuClientHelper.CreateQueueName(), false, false, true, null);

                foreach (var subscription in m_configsettings["client"]["subscriptions"])
                {

                    Log.Debug("Declaring Exchange...");
                    RabbitMQChannel.ExchangeDeclare(subscription.ToString(), "fanout", false, false, null);
                    Log.Debug("Done");

                    Log.Info("Binding queue {0} to exchange {1}", m_my_queue.QueueName, subscription);
                    RabbitMQChannel.QueueBind(m_my_queue.QueueName, subscription.ToString(), "");
                    m_rabbit_consumer = new EventingBasicConsumer(RabbitMQChannel);

                    m_rabbit_consumer.Received += SubscriptionReceived;
                    m_rabbit_consumer.Shutdown += SubscriptionShutdown;
                    m_rabbit_consumer.ConsumerCancelled += SubscriptionCancelled;

                    RabbitMQChannel.BasicConsume(m_my_queue.QueueName, true, m_rabbit_consumer);

                }

            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }

        }

        private static void PublishKeepAlive()
        {
            try
            {

                List<string> m_redactlist = null;
                var keepAlive = m_configsettings["client"];

                keepAlive["timestamp"] = SensuClientHelper.CreateTimeStamp();
                keepAlive["version"] = m_version_string;
                keepAlive["plugins"] = "";

                m_redactlist = SensuClientHelper.GetRedactlist((JObject)keepAlive);

                var payload = SensuClientHelper.RedactSensitiveInformaton(keepAlive, m_redactlist);

                Log.Debug("Publishing keepalive");

                var properties = new BasicProperties
                {
                    ContentType = "application/octet-stream",
                    Priority = 0,
                    DeliveryMode = 1
                };

                lock (m_lock_pulish)
                {
                    RabbitMQChannel.BasicPublish("", "keepalives", properties, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
                }

            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to publish KeepAlive!");
            }

        }

        private static void PublishResult(JObject check)
        {
            var payload = new JObject();
            payload["check"] = check;
            payload["client"] = m_configsettings["client"]["name"];

            try
            {

                Log.Info("Publishing Check Result {0}", JsonConvert.SerializeObject(payload, SerializerSettings));

                var properties = new BasicProperties
                {
                    ContentType = "application/octet-stream",
                    Priority = 0,
                    DeliveryMode = 1
                };

                lock (m_lock_pulish)
                {
                    RabbitMQChannel.BasicPublish("", "results", properties, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload)));
                }


            }
            catch (Exception ex)
            {
                Log.Error(ex, "Publishing Check Result failed: {0}",ex.Message);
            }
            finally
            {
                lock (m_lock_checkinprogress)
                {
                    if (ChecksInProgress.Contains(check["name"].ToString()))
                    { ChecksInProgress.Remove(check["name"].ToString()); }
                }
            }

        }

        public static void ProcessCheck(JObject check)
        {

            JToken command;

            Log.Debug("Processing check {0}", JsonConvert.SerializeObject(check, SerializerSettings));

            if (check.TryGetValue("command", out command))
            {
                if (m_configsettings["check"] != null && m_configsettings["check"].Contains(check["name"]))
                {
                    foreach (var thingie in m_configsettings["checks"][check["name"]])
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

        public static void ExecuteCheckCommand(JObject check)
        {

            string m_command = String.Empty;
            string m_checkcommand = String.Empty;
            string m_checkargs = String.Empty;

            bool m_skip_publish = false;
            string m_tokenparseerror = string.Empty;         
            Stopwatch m_stopwatch = new Stopwatch();
            TimeSpan m_check_timeout = TimeSpan.MinValue;

            Log.Debug("Attempting to execute check command {0}", JsonConvert.SerializeObject(check, SerializerSettings));

            try
            {
                if (check["name"] == null)
                {
                    throw new EmptyCheckNameException();
                }

                lock (m_lock_checkinprogress)
                {
                    if (ChecksInProgress.Contains(check["name"].ToString()))
                    {
                        throw new CheckInProgressException();
                    }
                    else
                    {
                        ChecksInProgress.Add(check["name"].ToString());
                    }
                }

                #region "split command and arguments and get check properties"

                m_command = SensuClientHelper.SubstitueCommandTokens(check, out m_tokenparseerror, (JObject)m_configsettings["client"]);
                m_command = m_command.Trim();

                if (check["timeout"] != null)
                {
                    m_check_timeout = TimeSpan.Parse(check["timeout"].ToString());
                }

                if (String.IsNullOrWhiteSpace(m_tokenparseerror))
                {

                    if (m_command.Contains(" "))
                    {
                        m_checkcommand = m_command.Split(" ".ToCharArray(), 2)[0];
                        m_checkargs = m_command.Split(" ".ToCharArray(), 2)[1];
                    }
                    else
                    {
                        m_checkcommand = m_command;
                    }

                }
                else
                {
                    throw new UnmatchedCommandTokensException();
                }

                #endregion

                #region "Plugin based check"

                if (m_program_base.Plugins.Count() != 0 && m_checkcommand.StartsWith("!"))
                {

                    Task<ExecuteResult> m_check_task = null;
                    CancellationTokenSource m_check_cancelationtokensrc = null;
                    CancellationToken m_check_cancelationtoken;
                    ExecuteResult m_plugin_return = null;

                    Log.Debug("Checking inf any plugin has register a handler for command {0}", m_checkcommand);

                    foreach (ISensuClientPlugin m_plugin in m_program_base.Plugins)
                    {

                        if (m_plugin.Handlers().Any(handler => handler.ToLower().Equals(m_checkcommand.ToLower())))
                        {
                            Log.Debug("Plugin {0} provides a handler for command {1}", m_plugin.Name(), m_checkcommand);

                            try
                            {

                                m_check_cancelationtokensrc = new CancellationTokenSource();
                                m_plugin_return = new ExecuteResult();
                                m_stopwatch.Reset();

                                Log.Debug("Passing Command to plugin {0}", m_plugin.Name());

                                Arguments m_command_args = new Arguments(Arguments.SplitCommandLine(m_checkargs));

                                m_stopwatch.Start();

                                m_check_cancelationtoken = m_check_cancelationtokensrc.Token;

                                m_check_task = Task<ExecuteResult>.Factory.StartNew(() => m_plugin.execute(m_checkcommand, m_command_args), m_check_cancelationtoken);

                                if (!m_check_timeout.Equals(TimeSpan.MinValue))
                                {
                                    m_check_task.Wait((int)m_check_timeout.TotalMilliseconds, m_check_cancelationtoken);
                                }
                                else
                                { 
                                    m_check_task.Wait();
                                }
               
                                if (!m_check_task.IsCompleted | m_check_task.IsCanceled)
                                { 
                                    throw new Exception("Check did not completed within the configured timeout!");
                                }

                                m_plugin_return = m_check_task.Result;

                                Log.Debug("Plugin Return: {0} / ExitCode: {1}", m_plugin_return.Output, m_plugin_return.ExitCode);

                                check["output"] = string.Format("{0}", m_plugin_return.Output);
                                check["status"] = m_plugin_return.ExitCode;

                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, ex.Message);
                                
                                //Print error of innerexception cause we using Tasks
                                if (ex.InnerException != null)
                                {
                                    Log.Error(ex, ex.InnerException.Message);
                                    throw new UnexpectedCheckException(ex.InnerException.Message);
                                }
                                else
                                {
                                    throw new UnexpectedCheckException(ex.Message);
                                }                             
                                
                            }
                            finally
                            {
                                m_stopwatch.Stop();

                                check["executed"] = SensuClientHelper.CreateTimeStamp();
                                check["duration"] = string.Format("{0:f3}", ((float)m_stopwatch.ElapsedMilliseconds) / 1000);

                                if (m_check_task != null)
                                {
                                    m_check_task.Dispose();
                                    m_check_cancelationtokensrc.Dispose();
                                }
                            }
                        }

                    }

                }
                #endregion

                else
                {
                    #region "Normal 'legacy' Check"

                    Process m_check_process = null;

                    ProcessStartInfo m_process_start_info = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(m_checkcommand))
                    {
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        Arguments = Environment.ExpandEnvironmentVariables(m_checkargs)

                    };

                    try
                    {
                        m_check_process = new Process { StartInfo = m_process_start_info };

                        m_check_process.Start();
                        m_stopwatch.Start();

                        if (!m_check_timeout.Equals(TimeSpan.MinValue))
                        {
                            if (!m_check_process.WaitForExit((int)m_check_timeout.TotalMilliseconds))
                            {
                                m_check_process.Kill();
                            }
                        }
                        else
                        {
                            m_check_process.WaitForExit();
                        }

                        check["output"] = string.Format("{0}{1}", m_check_process.StandardOutput.ReadToEnd(), m_check_process.StandardError.ReadToEnd());
                        check["status"] = m_check_process.ExitCode;

                    }
                    catch (Exception ex)
                    {
                        check["output"] = string.Format("Unexpected error: {0}", ex.Message);
                        check["status"] = 2;
                    }
                    finally
                    {
                        m_stopwatch.Stop();
                       
                        check["executed"] = SensuClientHelper.CreateTimeStamp();
                        check["duration"] = string.Format("{0:f3}", ((float)m_stopwatch.ElapsedMilliseconds) / 1000);

                        if (m_check_process !=null)
                        {
                            m_check_process.Dispose();
                        }
                    }

                    #endregion
                }

            }

            catch (CheckInProgressException)
            {
                m_skip_publish = true;
                Log.Warn("Previous check command execution in progress {0}", check["command"]);
            }

            catch (EmptyCheckNameException)
            {
                check["output"] = "Check didn't have a valid name";
                check["status"] = 3;
                check["handle"] = false;
            }

            catch (UnexpectedCheckException ex)
            {
                check["output"] = string.Format("Unexpected error: {0}", ex.Message);
                check["status"] = 2;
            }

            catch (UnmatchedCommandTokensException)
            {
                check["output"] = string.Format("Unmatched command tokens: {0}", m_tokenparseerror);
                check["status"] = 3;
                check["handle"] = false;
            }

            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
            }

            finally
            {

                if (!m_skip_publish)
                {
                    PublishResult(check);
                }
                else
                {
                    Log.Debug("Skipped Check Result publish - trying to rmeove from 'CheckInProgressList'");

                    lock (m_lock_checkinprogress)
                    {
                        if (ChecksInProgress.Contains(check["name"].ToString()))
                        { ChecksInProgress.Remove(check["name"].ToString()); }
                    }
                }

            }

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
            m_cancelationtokensrc.Cancel();

            lock (m_lock_pulish)
            {
                RabbitMQChannel.Close(200, "Bye");
            }

            RabbitMQConnection.Close();

            base.OnStop();
        }


        private static void Connect2RabbitMQ()
        {

            try
            {

                var connectionFactory = new ConnectionFactory
                {
                    HostName = m_configsettings["rabbitmq"]["host"].ToString(),
                    Port = int.Parse(m_configsettings["rabbitmq"]["port"].ToString()),
                    UserName = m_configsettings["rabbitmq"]["user"].ToString(),
                    Password = m_configsettings["rabbitmq"]["password"].ToString(),
                    VirtualHost = m_configsettings["rabbitmq"]["vhost"].ToString(),
                    TopologyRecoveryEnabled = true,
                    AutomaticRecoveryEnabled = true,
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(30),
                    RequestedHeartbeat = 30
                };

                m_rabbitmq_connection = connectionFactory.CreateConnection();
                m_rabbitmq_connection.ConnectionShutdown += RabbitMQConnection_Shutdown;
                m_rabbitmq_connection.RecoverySucceeded += RabbitMQConnecion_ReconnectSuccess;
                m_rabbitmq_connection.ConnectionRecoveryError += RabbitMQConnect_ReconnectFailed;
                m_rabbitmq_connection.ConnectionBlocked += RabbmitMQConnection_Blocked;
                m_rabbitmq_connection.ConnectionUnblocked += RabbmitMQConnection_UnBlocked;


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

        private static void RabbmitMQConnection_UnBlocked(object sender, EventArgs e)
        {
            Log.Warn("RabbitMQ Connection Unblocked!");
        }

        private static void RabbmitMQConnection_Blocked(object sender, ConnectionBlockedEventArgs e)
        {
            Log.Warn("RabbitMQ Connection blocked!");
        }


        #endregion

        #region "RabbitMQ Events"

        private static void SubscriptionReceived(object sender, BasicDeliverEventArgs e)
        {
            string m_payload = String.Empty;
            JObject m_check;

            try
            {

                Log.Debug("Received check request");

               
                if (e.Body != null)
                {

                    m_payload = Encoding.UTF8.GetString(e.Body);

                   if (!SensuClientHelper.TryParseData(m_payload, out m_check))
                    {
                        throw new JsonReaderException();
                    }

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
            Log.Error("SubscriptionCancelled: {0}", e.ConsumerTag.ToString());
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
