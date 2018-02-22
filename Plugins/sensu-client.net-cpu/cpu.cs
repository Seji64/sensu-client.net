using sensu_client.net.pluginterface;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;

namespace sensu_client.net.plugin
{
    [Export(typeof(ISensuClientPlugin))]
    public class cpu : ISensuClientPlugin
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
            List<double> m_measure_values = new List<double>();

            switch (handler)

            {
                case "!cpu-load":

                    PerformanceCounter m_perf_counter_cpu = null;
                   
                    if (!arguments.Exists ("critical") && !arguments.Exists("c"))
                    {
                        throw new ArgumentException("Argument 'critical threshold' (-c or --critical) is mandatory!");
                    }
                    else
                    {
                        if (arguments.Exists("critical"))
                        { Int32.TryParse(arguments.Single("critical"), out m_crit_threshold); }
                        else { Int32.TryParse(arguments.Single("c"), out m_crit_threshold);  }
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

                        m_perf_counter_cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");

                        for (int x = 1; x <= 5; x++)
                        {
                            m_measure_values.Add(m_perf_counter_cpu.NextValue());
                            System.Threading.Thread.Sleep(100);
                        }

                        if (m_measure_values == null || m_measure_values.Count == 0)
                        {
                            throw new Exception("failed to measure cpu usage");
                        }

                        m_measure_value = Math.Round((m_measure_values.Average()), 2);

                        if (m_measure_value >= m_crit_threshold)
                        {
                            m_result.Output = String.Format("CheckWindowsCpuLoad CRITICAL: CPU at {0}%", m_measure_value.ToString());
                            m_result.ExitCode = 2;
                        }

                        if (m_measure_value >= m_warn_threshold)
                        {
                            m_result.Output = String.Format("CheckWindowsCpuLoad WARNING: CPU at {0}%", m_measure_value.ToString());
                            m_result.ExitCode = 1;                            
                        }
                        else
                        {
                            m_result.Output = String.Format("CheckWindowsCpuLoad OK: CPU at {0}%", m_measure_value.ToString());
                            m_result.ExitCode = 0;
                        }

                    }

                    catch (Exception)
                    {
                        m_result.Output = String.Format("CheckWindowsCpuLoad UNKNOWN: Failed to measure cpu load!");
                        m_result.ExitCode = 3;
                    }
                    finally
                    {
                        if (m_perf_counter_cpu != null)
                        {
                            m_perf_counter_cpu.Dispose();
                        }               
                    }

                    return m_result;

                case "!cpu-queue":

                    PerformanceCounter m_perf_counter_system = null;

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

                        m_perf_counter_system = new PerformanceCounter("System", "Processor Queue Length");

                        for (int x = 1; x <= 5; x++)
                        {
                            m_measure_values.Add(m_perf_counter_system.NextValue());
                            System.Threading.Thread.Sleep(100);
                        }

                        if (m_measure_values == null || m_measure_values.Count == 0)
                        {
                            throw new Exception("failed to measure cpu queue");
                        }

                        m_measure_value = Math.Round((m_measure_values.Average()), 2);

                        if (m_measure_value >= m_crit_threshold)
                        {
                            m_result.Output = String.Format("CheckWindowsProcessorQueueLength CRITICAL: Processor Queue at {0}", m_measure_value.ToString());
                            m_result.ExitCode = 2;
                        }

                        if (m_measure_value >= m_warn_threshold)
                        {
                            m_result.Output = String.Format("CheckWindowsProcessorQueueLength WARNING: Processor Queue at {0}", m_measure_value.ToString());
                            m_result.ExitCode = 1;
                        }
                        else
                        {
                            m_result.Output = String.Format("CheckWindowsProcessorQueueLength OK: Processor Queue at {0}", m_measure_value.ToString());
                            m_result.ExitCode = 0;
                        }

                    }

                    catch (Exception)
                    {
                        m_result.Output = String.Format("CheckWindowsCpuLoad UNKNOWN: Failed to measure cpu queue!");
                        m_result.ExitCode = 3;
                    }
                    finally
                    {
                        if (m_perf_counter_system != null)
                        {
                            m_perf_counter_system.Dispose();
                        }
                    }

                    return m_result;


                default:

                    throw new Exception("Malformed check command!");

            }

        }

        public string GUID()
        {
            return "D25087E3-6881-4D81-8131-9312361F1D13";
        }

        public List<string> Handlers()
        {

            List<string> m_handlers = new List<string>();

            m_handlers.Add("!cpu-load");
            m_handlers.Add("!cpu-queue");
            m_handlers.Add("!cpu-load-metric");

            return m_handlers;
        }

        public void Initialize()
        {
            //nothing to do
        }

        public void Dispose()
        {
            //do nothing
        }

        public string Name()
        {
            return "CPUCheckPlugin";
        }

        public string Version()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }
    }
}
