using sensu_client.net.pluginterface;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Management;

namespace sensu_client.net.plugin
{
    [Export(typeof(ISensuClientPlugin))]
    public class memory : ISensuClientPlugin
    {
        public string Author()
        {
            return "Tim Hofmann";
        }

        public ExecuteResult execute(string handler, Arguments arguments)
        {

            Int32 m_crit_threshold = 0;
            Int32 m_warn_threshold = 0;
            double m_measure_value = -1;
            ExecuteResult m_result = new ExecuteResult();

            switch (handler)

            {
                case "!memory-usage":


                    ManagementObjectSearcher m_wmi_searcher = null;

                    if (!arguments.Exists("critical") && !arguments.Exists("c"))
                    {
                        throw new ArgumentException("Argument 'critical threshold' (-c or --critical) is mandatory!");
                    }
                    else
                    {
                        if (arguments.Exists("critical"))
                        { Int32.TryParse(arguments.Single("critical"), out m_crit_threshold); }
                        else { Int32.TryParse(arguments.Single("c"), out m_crit_threshold); }
                    }

                    if (!arguments.Exists("warning") && !arguments.Exists("w"))
                    {
                        throw new ArgumentException("Argument 'warning threshold' (-w or --warning) is mandatory!");
                    }
                    else
                    {
                        if (arguments.Exists("warning"))
                        { Int32.TryParse(arguments.Single("warning"), out m_warn_threshold); }
                        else { Int32.TryParse(arguments.Single("w"), out m_warn_threshold); }
                    }

                    try

                    {

                        m_wmi_searcher = new ManagementObjectSearcher("select * from Win32_OperatingSystem");

                        var m_measure_values = m_wmi_searcher.Get().Cast<ManagementObject>().Select(mo => new { FreePhysicalMemory = Double.Parse(mo["FreePhysicalMemory"].ToString()), TotalVisibleMemorySize = Double.Parse(mo["TotalVisibleMemorySize"].ToString())}).FirstOrDefault();

                        if (m_measure_values != null)
                        {
                            m_measure_value = Math.Round(((m_measure_values.TotalVisibleMemorySize - m_measure_values.FreePhysicalMemory) / m_measure_values.TotalVisibleMemorySize) * 100, 2);

                            if (m_measure_value >= m_crit_threshold)
                            {
                                m_result.Output = String.Format("CheckWindowsRAMLoad CRITICAL: RAM at {0}%", m_measure_value.ToString());
                                m_result.ExitCode = 2;
                            }

                            if (m_measure_value >= m_warn_threshold)
                            {
                                m_result.Output = String.Format("CheckWindowsRAMLoad WARNING: RAM at {0}%", m_measure_value.ToString());
                                m_result.ExitCode = 1;
                            }
                            else
                            {
                                m_result.Output = String.Format("CheckWindowsRAMLoad OK: RAM at {0}%", m_measure_value.ToString());
                                m_result.ExitCode = 0;
                            }
                        }
                        else
                        {
                            throw new Exception("failed to measure memory usage");
                        }

                    }
                    catch (Exception)
                    {
                        m_result.Output = String.Format("CheckWindowsCpuLoad UNKNOWN: Failed to measure cpu load!");
                        m_result.ExitCode = 3;
                    }
                    finally
                    {
                        if (m_wmi_searcher != null)
                        {
                            m_wmi_searcher.Dispose();
                        }
                    }

                    return m_result;

                default:

                    throw new Exception("Malformed check command!");

            }

        }

        public string GUID()
        {
            return "E80D6ADD-CA00-4EA4-BDCA-5617AD49D99C";
        }

        public List<string> Handlers()
        {
            List<string> m_handlers = new List<string>();

            m_handlers.Add("!memory-usage");

            return m_handlers;
        }

        public void Initialize()
        {
            //nothing to do
        }

        public string Name()
        {
            return "MemoryCheckPlugin";
        }

        public string Version()
        {
            return "0.1";
        }
    }
}
