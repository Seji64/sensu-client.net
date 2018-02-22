using NLog;
using sensu_client.net.pluginterface;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Data.SqlClient;
using System.Text;

namespace sensu_client.net.plugin
{
    [Export(typeof(ISensuClientPlugin))]
    public class mssql : ISensuClientPlugin
    {

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public string Author()
        {
            return "Tim Hofmann";
        }


        public ExecuteResult execute(string handler, Arguments arguments)
        {

            string m_sql_user = String.Empty;
            string m_sql_user_password = String.Empty;
            string m_sql_server = "(local)";
            ExecuteResult m_result = new ExecuteResult();

            switch (handler)

            {

                case "!mssql-metric-performance-metrics":

                    Helper.ParseArguments(arguments, out m_sql_server, out m_sql_user, out m_sql_user_password);

                    using (SqlConnection m_sql_conn = SQLHelper.GetSqlConnection(m_sql_server, m_sql_user, m_sql_user_password))
                    {

                        m_sql_conn.Open();

                        if (m_sql_conn.State != System.Data.ConnectionState.Open)
                        {
                            throw new Exception("failed to open sql connection!");
                        }


                        StringBuilder m_database_stats = SQLHelper.GetPerformanceMetrics(m_sql_conn);

                        if (m_database_stats == null || m_database_stats.Length == 0)
                        {
                            throw new Exception("failed to get performance metrics!");
                        }
                        else
                        {
                            m_result.ExitCode = 0;
                            m_result.Output = m_database_stats.ToString();
                        }

                    }

                    return m_result;


                case "!mssql-metric-performance-counters":

                    Helper.ParseArguments(arguments, out m_sql_server, out m_sql_user, out m_sql_user_password);

                    using (SqlConnection m_sql_conn = SQLHelper.GetSqlConnection(m_sql_server, m_sql_user, m_sql_user_password))
                    {

                        m_sql_conn.Open();

                        if (m_sql_conn.State != System.Data.ConnectionState.Open)
                        {
                            throw new Exception("failed to open sql connection!");
                        }


                        StringBuilder m_database_stats = SQLHelper.GetPerformanceCounters(m_sql_conn);

                        if (m_database_stats == null || m_database_stats.Length == 0)
                        {
                            throw new Exception("failed to get performance counters!");
                        }
                        else
                        {
                            m_result.ExitCode = 0;
                            m_result.Output = m_database_stats.ToString();
                        }

                    }

                    return m_result;

                case "!mssql-metric-memory-clerk":

                    Helper.ParseArguments(arguments, out m_sql_server, out m_sql_user, out m_sql_user_password);

                    using (SqlConnection m_sql_conn = SQLHelper.GetSqlConnection(m_sql_server, m_sql_user, m_sql_user_password))
                    {

                        m_sql_conn.Open();

                        if (m_sql_conn.State != System.Data.ConnectionState.Open)
                        {
                            throw new Exception("failed to open sql connection!");
                        }

                        StringBuilder m_database_stats = SQLHelper.GetMemoryClerk(m_sql_conn);

                        if (m_database_stats == null || m_database_stats.Length == 0)
                        {
                            throw new Exception("failed to get memory clerk stats!");
                        }
                        else
                        {
                            m_result.ExitCode = 0;
                            m_result.Output = m_database_stats.ToString();
                        }

                    }

                    return m_result;

                case "!mssql-metric-database-size":

                    Helper.ParseArguments(arguments, out m_sql_server, out m_sql_user, out m_sql_user_password);

                    using (SqlConnection m_sql_conn = SQLHelper.GetSqlConnection(m_sql_server, m_sql_user, m_sql_user_password))
                    {

                        m_sql_conn.Open();

                        if (m_sql_conn.State != System.Data.ConnectionState.Open)
                        {
                            throw new Exception("failed to open sql connection!");
                        }

                        StringBuilder m_database_stats = SQLHelper.GetDatabaseSize(m_sql_conn);

                        if (m_database_stats == null || m_database_stats.Length == 0)
                        {
                            throw new Exception("failed to get database size stats!");
                        }
                        else
                        {
                            m_result.ExitCode = 0;
                            m_result.Output = m_database_stats.ToString();
                        }

                    }

                    return m_result;


                case "!mssql-metric-database-io":

                    Helper.ParseArguments(arguments, out m_sql_server, out m_sql_user, out m_sql_user_password);

                    using (SqlConnection m_sql_conn = SQLHelper.GetSqlConnection(m_sql_server, m_sql_user, m_sql_user_password))
                    {

                        m_sql_conn.Open();

                        if (m_sql_conn.State != System.Data.ConnectionState.Open)
                        {
                            throw new Exception("failed to open sql connection!");
                        }

                        StringBuilder m_database_stats = SQLHelper.GetDatabaseIO(m_sql_conn);

                        if (m_database_stats == null || m_database_stats.Length == 0)
                        {
                            throw new Exception("failed to get database io stats!");
                        }
                        else
                        {
                            m_result.ExitCode = 0;
                            m_result.Output = m_database_stats.ToString();
                        }

                    }

                    return m_result;         

                case "!mssql-metric-wait-stats":

                    Helper.ParseArguments(arguments, out m_sql_server, out m_sql_user, out m_sql_user_password);

                    using (SqlConnection m_sql_conn = SQLHelper.GetSqlConnection(m_sql_server, m_sql_user, m_sql_user_password))
                    {

                        m_sql_conn.Open();

                        if (m_sql_conn.State != System.Data.ConnectionState.Open)
                        {
                            throw new Exception("failed to open sql connection!");
                        }

                        StringBuilder m_database_stats = SQLHelper.GetWaitStats(m_sql_conn);

                        if (m_database_stats == null || m_database_stats.Length == 0)
                        {
                            throw new Exception("failed to get wait stats!");
                        }
                        else
                        {
                            m_result.ExitCode = 0;
                            m_result.Output = m_database_stats.ToString();
                        }

                    }

                    return m_result;

                case "!mssql-metric-server-properties":

                    Helper.ParseArguments(arguments, out m_sql_server, out m_sql_user, out m_sql_user_password);

                    using (SqlConnection m_sql_conn = SQLHelper.GetSqlConnection(m_sql_server, m_sql_user, m_sql_user_password))
                    {

                        m_sql_conn.Open();

                        if (m_sql_conn.State != System.Data.ConnectionState.Open)
                        {
                            throw new Exception("failed to open sql connection!");
                        }

                        StringBuilder m_database_stats = SQLHelper.GetServerProperties(m_sql_conn);

                        if (m_database_stats == null || m_database_stats.Length == 0)
                        {
                            throw new Exception("failed to get database properties!");
                        }
                        else
                        {
                            m_result.ExitCode = 0;
                            m_result.Output = m_database_stats.ToString();
                        }

                    }

                    return m_result;

                case "!mssql-check-availability-group":

                    Helper.ParseArguments(arguments, out m_sql_server, out m_sql_user, out m_sql_user_password);

                    using (SqlConnection m_sql_conn = SQLHelper.GetSqlConnection(m_sql_server, m_sql_user, m_sql_user_password))
                    {

                        m_sql_conn.Open();

                        if (m_sql_conn.State != System.Data.ConnectionState.Open)
                        {
                            throw new Exception("failed to open sql connection!");
                        }

                        m_result = SQLHelper.CheckAvailbilityGroups(m_sql_conn);

                        if (m_result == null)
                        {
                            m_result.ExitCode = 3;
                            m_result.Output = "CheckMSSQLAvailbilityGroup UNKNOWN: Failed to get AvailibilityGroup Status!";

                        }

                    }

                    return m_result;


                case "!mssql-check-availability-group-databases":

                    Helper.ParseArguments(arguments, out m_sql_server, out m_sql_user, out m_sql_user_password);

                    using (SqlConnection m_sql_conn = SQLHelper.GetSqlConnection(m_sql_server, m_sql_user, m_sql_user_password))
                    {

                        m_sql_conn.Open();

                        if (m_sql_conn.State != System.Data.ConnectionState.Open)
                        {
                            throw new Exception("failed to open sql connection!");
                        }

                        m_result = SQLHelper.CheckAvailbilityGroupDatabases(m_sql_conn);

                        if (m_result == null)
                        {
                            m_result.ExitCode = 3;
                            m_result.Output = "CheckMSSQLAvailbilityGroupDatabases UNKNOWN: Failed to get AvailibilityGroup Databases Status!";

                        }

                    }

                    return m_result;

                default:

                    throw new Exception(String.Format("Malformed check or metric command! - Cannot handle {0}",handler));

            }
        }

        public string GUID()
        {
            return "5245A0C6-8877-49FC-8F49-1DA2B988EA47";
        }

        public List<string> Handlers()
        {
            List<string> m_handlers = new List<string>();

            m_handlers.Add("!mssql-metric-performance-counters");
            m_handlers.Add("!mssql-metric-performance-metrics");
            m_handlers.Add("!mssql-metric-memory-clerk");
            m_handlers.Add("!mssql-metric-database-size");
            m_handlers.Add("!mssql-metric-database-io");
            m_handlers.Add("!mssql-metric-wait-stats");
            m_handlers.Add("!mssql-metric-server-properties");
            m_handlers.Add("!mssql-check-availability-group");
            m_handlers.Add("!mssql-check-availability-group-databases");

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
            return "MSSQLPlugin";
        }

        public string Version()
        {
            return "0.5";
        }


    }
}
