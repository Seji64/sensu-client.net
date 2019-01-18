using sensu_client.net.pluginterface;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net_smb
{
    [Export(typeof(ISensuClientPlugin))]
    public class smb : ISensuClientPlugin
    {

        List<string> m_counters;

        public string Author()
        {
            return "Tim Hofmann";
        }

        public void Dispose()
        {
            //nothing to do
        }

        public ExecuteResult execute(string handler, Arguments arguments)
        {

            ExecuteResult m_result = new ExecuteResult();

            switch (handler)

            {
                case "!smb-client-share-metric":
       
                    PerformanceCounter m_perf_counter_smb = null;
                    PerformanceCounterCategory m_perf_category = null;
                    StringBuilder m_metrics = new StringBuilder();

                    try
                    {
    
                        if (m_counters == null)
                        {
                            throw new Exception("Counters list is empty!");
                        }

                        foreach (string m_counter_name in m_counters)
                        {
                           
                            m_perf_category = new PerformanceCounterCategory("SMB Client Shares");

                            foreach (string m_counter_instance in m_perf_category.GetInstanceNames())
                            {

                                m_perf_counter_smb = new PerformanceCounter(m_perf_category.CategoryName, m_counter_name,true);
                                m_perf_counter_smb.InstanceName = m_counter_instance;

                                CounterSample cs1 = m_perf_counter_smb.NextSample();
                                Task.Delay(100).Wait();
                                CounterSample cs2 = m_perf_counter_smb.NextSample();

                                float m_value = CounterSample.Calculate(cs1, cs2);

                                string clean_counter_name = m_counter_name.ToLower();
                                string clean_instance_name = m_counter_instance.ToLower();

                                clean_instance_name = clean_instance_name.Replace("Avg.", "avg");
                                clean_instance_name = clean_instance_name.Replace("/", "_per_");
                                clean_instance_name = clean_instance_name.Replace(" ", "_");

                                m_metrics.AppendLine(String.Format("{0}.smb.{1}.{2} {3} {4}", Helper.GetFQDN(), clean_instance_name, clean_counter_name, m_value, Helper.CreateTimeStamp().ToString()));

                            }

                        }

                        m_result.Output = m_metrics.ToString();
                        m_result.ExitCode = 0;


                    }
                    catch (Exception ex)
                    {
                        m_result.Output = String.Format("SMBCheckPlugin UNKNOWN: Failed to get smb share metrics! | {0}",ex.Message);
                        m_result.ExitCode = 3;
                    }
                    finally
                    {
                        if (m_perf_counter_smb != null)
                        {
                            m_perf_counter_smb.Dispose();
                        }
                    }

                    return m_result;

                default:

                    throw new Exception("Malformed check command!");
            }
         }

        public string GUID()
        {
            return "A7B0FBCC-6601-4A4F-94C3-184C7F85C0F8";
        }

        public List<string> Handlers()
        {
            List<string> m_handlers = new List<string>();

            m_handlers.Add("!smb-client-share-metric");

            return m_handlers;
        }

        public void Initialize()
        {

            m_counters = new List<string>
            {
                "Credit Stalls/sec",
                "Metadata Requests/sec",
                "Avg. Data Queue Length",
                "Avg. Write Queue Length",
                "Avg. Read Queue Length",
                "Current Data Queue Length",
                "Avg. sec/Data Request",
                "Avg. Data Bytes/Request",
                "Data Requests/sec",
                "Data Bytes/sec",
                "Avg. sec/Write",
                "Avg. sec/Read",
                "Avg. Bytes/Write",
                "Write Requests/sec",
                "Read Requests/sec",
                "Write Bytes/sec",
                "Read Bytes/sec"
            };
        }

        public string Name()
        {
            return "SMBCheckPlugin";
        }

        public string Version()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }
    }
}
