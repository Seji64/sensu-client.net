using sensu_client.net.pluginterface;
using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;

namespace sensu_client.net_disk
{
    [Export(typeof(ISensuClientPlugin))]
    public class disk : ISensuClientPlugin
    {
        public string Author()
        {
            return "Tim Hofmann";
        }

        public ExecuteResult execute(string handler, Arguments arguments)
        {
            Int32 m_crit_threshold = 0;
            Int32 m_warn_threshold = 0;

            string m_disks_crit = String.Empty;
            string m_disks_warn = String.Empty;
            ExecuteResult m_result = new ExecuteResult();

            switch (handler)
            {

                case "!disk-usage":

                    List<DriveMeasure> m_measure_values = new List<DriveMeasure>();

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

                       foreach(DriveInfo m_drive in DriveInfo.GetDrives())
                        {
                           if( m_drive .DriveType ==  DriveType.Fixed && m_drive .IsReady)

                            {

                                DriveMeasure m_drive_measure = new DriveMeasure();

                                m_drive_measure.UsedPercentage = Math.Round((double)((m_drive.TotalSize - m_drive.AvailableFreeSpace) / m_drive.TotalSize) * 100, 2);
                                m_drive_measure.FreeSpace = m_drive.AvailableFreeSpace;
                                m_drive_measure.Size = m_drive.TotalSize;
                                m_drive_measure.ID = m_drive.Name.Replace(System.IO.Path.PathSeparator.ToString(),"");

                                if (m_drive_measure.UsedPercentage >= m_crit_threshold)
                                {
                                    m_drive_measure.DriveState = DriveMeasure.State.CRITICAL;
                                }

                                if (m_drive_measure.UsedPercentage >= m_warn_threshold)
                                {
                                    m_drive_measure.DriveState = DriveMeasure.State.WARNING;
                                }
                                else
                                {
                                    m_drive_measure.DriveState = DriveMeasure.State.OK;
                                }

                                m_measure_values.Add(m_drive_measure);

                            }
                        }

                       if (m_measure_values == null || m_measure_values.Count ==0)
                        {
                            throw new Exception("failed to measure disk usage");
                        }

                        foreach (var disk in m_measure_values.Where((m_disk) => m_disk.DriveState.Equals(DriveMeasure.State.WARNING)))
                        {
                            m_disks_warn = String.Format("{0}{1}", m_disks_warn, disk.ToString());
                        }

                        foreach (var disk in m_measure_values.Where((m_disk) => m_disk.DriveState.Equals(DriveMeasure.State.CRITICAL)))
                        {
                            m_disks_crit = String.Format("{0}{1}", m_disks_crit, disk.ToString());
                        }

                        if (m_measure_values .Where((m_disk) => m_disk.DriveState.Equals (DriveMeasure.State.CRITICAL)).Count() != 0)
                        {

                            m_result.ExitCode = 2;

                            if (m_measure_values.Where((m_disk) => m_disk.DriveState.Equals(DriveMeasure.State.WARNING)).Count() != 0)
                            {
                                m_result.Output = String.Format("CheckDisk CRITICAL: {0} disk in critical state:`n{1};`n{2} disks in warning state:`n{3}", m_measure_values.Where((m_disk) => m_disk.DriveState.Equals(DriveMeasure.State.CRITICAL)).Count(), m_disks_crit, m_measure_values.Where((m_disk) => m_disk.DriveState.Equals(DriveMeasure.State.WARNING)).Count(), m_disks_warn);
                            }
                            else
                            {
                                m_result.Output = String.Format("CheckDisk CRITICAL: {0} disk in critical state `n{1};", m_measure_values.Where((m_disk) => m_disk.DriveState.Equals(DriveMeasure.State.CRITICAL)).Count(), m_disks_crit);
                            }
                        
                        }
                        else if (m_measure_values.Where((m_disk) => m_disk.DriveState.Equals(DriveMeasure.State.WARNING)).Count() != 0)
                        {
                            m_result.ExitCode = 1;
                            m_result.Output = String.Format("CheckDisk WARNING: {0} disk in warning state `n{1};", m_measure_values.Where((m_disk) => m_disk.DriveState.Equals(DriveMeasure.State.WARNING)).Count(), m_disks_warn);
                        }

                        m_result.ExitCode = 0;
                        m_result.Output = String.Format("CheckDisk OK: All disk usage under {0}%",m_warn_threshold);

                    }
                    catch (Exception)
                    {
                        m_result.Output = String.Format("CheckDisk UNKNOWN: Failed to measure disk usage!");
                        m_result.ExitCode = 3;
                    }

                    return m_result;

                default:

                    throw new Exception("Malformed check command!");

            }

        }

        public string GUID()
        {
            return "5D8C1080-FCD2-4C8C-95E4-01F40F93B9B2";
        }

        public List<string> Handlers()
        {
            List<string> m_handlers = new List<string>();

            m_handlers.Add("!disk-usage");

            return m_handlers;
        }

        public void Initialize()
        {
           //nothing to do
        }

        public string Name()
        {
            return "DiskCheckPlugin";
        }

        public string Version()
        {
            return "0.1";
        }
    }

    public static class DriveMeasureExtension
    {

        public enum SizeUnits
        {
            Byte, KB, MB, GB, TB, PB, EB, ZB, YB
        }

        public static string ToSize(this double value, SizeUnits unit)
        {
            return (value / (double)Math.Pow(1024, (Int64)unit)).ToString("0.00");
        }
    }

    public class DriveMeasure

    {
        public enum State
        {
            OK,WARNING,CRITICAL,UNKNOWN
        }

        public State DriveState;

        public string ID;

        public double FreeSpace;

        public double Size;

        public double UsedPercentage;

        public override string ToString()
        {
            return String.Format("({0}) {1}%, FREE: {2} GB, SIZE: {3} GB`n",this.ID,this.UsedPercentage , DriveMeasureExtension.ToSize(this.FreeSpace,DriveMeasureExtension.SizeUnits.GB), DriveMeasureExtension.ToSize(this.Size, DriveMeasureExtension.SizeUnits.GB));
        }

    }

}
