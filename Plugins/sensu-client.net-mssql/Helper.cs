using sensu_client.net.pluginterface;
using System;

namespace sensu_client.net.plugin
{
    public static class Helper
    {

        public static void ParseArguments (Arguments arguments, out string m_sql_server, out string m_sql_user, out string m_sql_user_password)
        {

            m_sql_server = "(local)";
            m_sql_user = String.Empty;
            m_sql_user_password = String.Empty;

            if ((arguments.Exists("user") || arguments.Exists("u")) && (arguments.Exists("password") || arguments.Exists("p")))
            {

                if (arguments.Exists("user"))
                {
                    m_sql_user = arguments.Single("user");

                }
                else
                {
                    m_sql_user = arguments.Single("u");
                }

                if (arguments.Exists("password"))
                {
                    m_sql_user_password = arguments.Single("password");

                }
                else
                {
                    m_sql_user_password = arguments.Single("p");
                }

            }

            if (arguments.Exists("server") || arguments.Exists("s"))
            {

                if (arguments.Exists("server"))
                {
                    m_sql_server = arguments.Single("server");

                }
                else
                {
                    m_sql_server = arguments.Single("s");
                }

            }

        }

        public static long CreateTimeStamp()
        {
            return Convert.ToInt64(Math.Round((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds, MidpointRounding.AwayFromZero));
        }

        public static string GetFQDN()
        {
            string domainName = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName;
            string hostName = System.Net.Dns.GetHostName();

            if (!hostName.EndsWith(domainName))  // if hostname does not already include domain name
            {
                hostName += "." + domainName;   // add the domain name part
            }

            return hostName;                    // return the fully qualified name
        }

    }
}
