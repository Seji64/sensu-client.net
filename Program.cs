using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ServiceProcess;
using System.Text;
using NLog;
using sensu_client.net.Exceptions;
using sensu_client.net.pluginterface;

namespace sensu_client.net
{
    class Program
    {
        [ImportMany(typeof(ISensuClientPlugin))]
        private ISensuClientPlugin[] m_plugins = null;
        static Logger _log;

        public ISensuClientPlugin[] Plugins => m_plugins;

        static void Main()
        {
            _log = LogManager.GetCurrentClassLogger();
#if DEBUG
            LogManager.GlobalThreshold = LogLevel.Trace;
#endif

            new Program().Run();

        }
        static void CurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            _log.Error("Global Exception handler called with exectpion: {0}", e.ExceptionObject);
        }

        public void LoadPlugins()
        {

            #region "Loading Plugins"

            try
            {

                _log.Info("Trying to load Plugins...");

                string m_plugin_dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "plugins");

                var catalog = new DirectoryCatalog(m_plugin_dir);
                var container = new CompositionContainer(catalog);
                container.ComposeParts(this);

                foreach (ISensuClientPlugin m_plugin in m_plugins)
                {
                    StringBuilder m_plugin_info = new StringBuilder();

                    try
                    {

                        if (m_plugin.Name() != null && !String.IsNullOrWhiteSpace(m_plugin.Name()) && m_plugin.GUID() != null && !String.IsNullOrWhiteSpace(m_plugin.GUID().ToString()))
                        {

                            if (m_plugin.Handlers().Count == 0)
                            {
                                throw new InvalidPluginException(String.Format("Plugin {0} / {1} has no valid Handlers to register!", m_plugin.Name(), m_plugin.GUID().ToString()));
                            }
                            else
                            {

                                m_plugin_info.AppendLine(String.Format("Found plugin {0}", m_plugin.Name()));
                                m_plugin_info.AppendLine(String.Format("Plugin ID: {0}", m_plugin.GUID()));
                                m_plugin_info.AppendLine(String.Format("Plugin Author: {0}", m_plugin.Author()));
                                m_plugin_info.AppendLine(String.Format("Plugin Version: {0}", m_plugin.Version()));
                                m_plugin_info.AppendLine("Plugin Handlers:");

                                foreach (string m_handler in m_plugin.Handlers())
                                {
                                    m_plugin_info.AppendLine(String.Format("- {0}", m_handler.ToString()));
                                }

                                _log.Info(m_plugin_info.ToString());

                                try
                                {

                                    _log.Debug("Calling 'Initialize' of plugin {0}", m_plugin.Name());

                                    m_plugin.Initialize();

                                    _log.Info("Plugin {0} successfully loaded!", m_plugin.Name());

                                }
                                catch (Exception ex)
                                {
                                    _log.Error(ex, "Plugin initializion failed!");
                                }

                            }

                        }
                        else
                        {
                            throw new InvalidPluginException("Invalid Plugin found!");
                        }

                    }
                    catch (InvalidPluginException ex)
                    {
                        _log.Error(ex, ex.Message);
                    }

                }

                _log.Info("Done - loaded {0} Plugin(s)!", m_plugins.Length);

            }
            catch (CompositionException compositionException)
            {
                Console.WriteLine(compositionException.ToString());
            }

            #endregion

        }

        void Run()
        {

            AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;

            if (Environment.UserInteractive)
            {
                SensuClient.Start();
                Console.CancelKeyPress += delegate
                {
                    _log.Info("Cancel Key Pressed. Shutting Down.");
                };
            }
            else
            {
                var servicesToRun = new ServiceBase[] { new SensuClient() };
                ServiceBase.Run(servicesToRun);
            }


        }

    }
}
