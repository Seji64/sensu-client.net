using sensu_client.net.pluginterface;
using sensu_client.net_services;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.ServiceProcess;
using System.Text;

namespace sensu_client.net.plugin
{
    [Export(typeof(ISensuClientPlugin))]
    public class services : ISensuClientPlugin
    {

        private List<string> ParseServices(Arguments m_arguments,string argument_name_full, string argument_name_short)
        {

            string m_services_raw = String.Empty;
            List<string> m_services = new List<string>();

            if (m_arguments.Exists(argument_name_full))
            {
                m_services_raw = m_arguments.Single(argument_name_full).ToString();
            }
            else
            {
                m_services_raw = m_arguments.Single(argument_name_short).ToString();
            }

            if (m_services_raw.Contains(","))
            {
                foreach (string m_service in m_services_raw.Split(Char.Parse(",")))
                {
                    m_services.Add(m_service);

                }
            }
            else
            {
                m_services.Add(m_services_raw);
            }

            return m_services;

        }

        private List<KeyValuePair<ServiceController, StartMode>> GetAllAutomaticServices(List<string> m_excluded_services)
        {

            List<KeyValuePair<ServiceController, StartMode>> m_services = new List<KeyValuePair<ServiceController, StartMode>>();

            foreach (ServiceController m_service in ServiceController.GetServices())
            {

                StartMode m_service_start_mode = StartMode.Undefinied;

                if (!m_excluded_services.Any(m_svcname => m_svcname.ToLower().Equals(m_service.ServiceName.ToLower())))
                {

                    m_service_start_mode = ServiceHelper.GetStartMode(m_service.ServiceName);

                    if (m_service_start_mode == StartMode.Automatic || m_service_start_mode == StartMode.AutomaticDelayed)

                    {
                        m_services.Add(new KeyValuePair<ServiceController, StartMode>(m_service, m_service_start_mode));
                    }

                }

            }

            return m_services;
        }

        private KeyValuePair<ServiceController, StartMode> GetServiceByName(string m_servicename)
        {

            KeyValuePair<ServiceController, StartMode> m_service = new KeyValuePair<ServiceController, StartMode>(null,StartMode.Undefinied);

            try
            {
                ServiceController m_tmp_service = ServiceController.GetServices().Where(m_svc => m_svc.ServiceName.ToLower().Equals(m_servicename.ToLower())).First();

                if (m_tmp_service != null)
                {
                    m_service = new KeyValuePair<ServiceController, StartMode>(m_tmp_service, ServiceHelper.GetStartMode(m_tmp_service.ServiceName));
                }

            }
            catch
            {
                //Service not found
            }

            return m_service;

        }

        public string Author()
        {
            return "Tim Hofmann";
        }

        public ExecuteResult execute(string handler, Arguments arguments)
        {
            
            switch(handler)
            {

                case "!services-state":

                    bool m_check_all = false;
                    List<string> m_service_names = new List<string>();
                    List<string> m_service_exclude_names = new List<string>();
                    List<KeyValuePair<ServiceController, StartMode>> m_services = new List<KeyValuePair<ServiceController, StartMode>>();
                    ExecuteResult m_result = new ExecuteResult();

                    if (arguments.Exists("exclude") || arguments.Exists("e"))
                    {
                      m_service_exclude_names = ParseServices(arguments, "exclude", "e");
                    }

                    if (arguments.Exists("all") || arguments.Exists("a"))
                    {
                        m_check_all = true;

                        if (arguments.Exists("services") && arguments.Exists("s"))
                        {
                            m_service_names = ParseServices(arguments, "services", "s");
                        }

                    }
                    else
                    {

                        if (!arguments.Exists("services") && !arguments.Exists("s"))
                        {
                            throw new ArgumentException("Argument 'services' (-s or --services) is mandatory if don't use '--all' switch!");
                        }
                        else
                        {
                            m_service_names = ParseServices(arguments, "services", "s");
                        }

                    }


                    if (!m_check_all && m_service_names.Count == 0)
                    {
                        throw new ArgumentException("Malformed Arguments");
                    }

                    m_services = GetAllAutomaticServices(m_service_exclude_names);

                    foreach(string m_service_name in m_service_names)
                    {
                        KeyValuePair<ServiceController, StartMode> m_service;

                        m_service = GetServiceByName(m_service_name);

                        if(m_service.Key != null || !m_services.Contains (m_service))

                        {
                            m_services.Add(m_service);
                        }

                    }

                    //Check if OK or Critical
                    if (m_services.Where(m_service => m_service.Key.Status != ServiceControllerStatus.Running).Count() != 0)
                    {
                        StringBuilder m_ciritical_services = new StringBuilder();

                        foreach (string m_svc in m_services.Where(m_service => m_service.Key.Status != ServiceControllerStatus.Running).Select(m_svc => m_svc.Key.ServiceName).ToArray())
                        {
                            m_ciritical_services.AppendLine(m_svc);
                        }
                        

                        m_result.Output = String.Format("CheckService CRITICAL: {0} Services are not in state 'running':`n{1}", m_services.Where(m_service => m_service.Key.Status != ServiceControllerStatus.Running).Count(), m_ciritical_services.ToString());
                        m_result.ExitCode = 2;
                    }
                    else
                    {
                        m_result.Output = String.Format("CheckService OK: All defined services are in state 'running'!");
                        m_result.ExitCode = 0;
                    }

                    return m_result;

                default:

                    throw new Exception("Malformed check command!");

            }

        }

        public string GUID()
        {
            return "954B0A03-5179-4018-AD97-375C72B054CC";
        }

        public List<string> Handlers()
        {
            List<string> m_handlers = new List<string>();

            m_handlers.Add("!services-state");

            return m_handlers;
        }

        public void Initialize()
        {
            //nothing to do
        }

        public string Name()
        {
            return "CheckServicesPlugin";
        }

        public string Version()
        {
            return "0.2";
        }
    }
}
