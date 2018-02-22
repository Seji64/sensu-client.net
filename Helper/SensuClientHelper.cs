using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace sensu_client.net
{
    public static class SensuClientHelper
    {

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        public const string ErroTextDefaultValueMissing = "Default missing:";
        public const string ErroTextDefaultDividerMissing = "Default divider missing";

        public static bool ValidateCheckResult(JObject check)
        {
            var regexItem = new Regex(@"/^[\w\.-]+$/", RegexOptions.Compiled);

            if (regexItem.IsMatch(check["name"].ToString())) return false;
            if (check["output"].Type != JTokenType.String) return false;
            if (check["status"].Type != JTokenType.Integer) return false;

            return true;
        }

        public static bool TryParseData(String data, out JObject result)
        {
            bool parseResult = false;
            JObject json = null;

            try
            {
                json = JObject.Parse(data);
                parseResult = true;
            }
            catch (Exception)
            {
                Log.Warn("Failed to parse to JObject: {0}", data);
            }

            result = json;
            return parseResult;

        }

        public static string CreateQueueName()
        {
            return String.Format("{0}-{1}-{2}", GetFQDN(), System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(), CreateTimeStamp());
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

        public static long CreateTimeStamp()
        {
            return Convert.ToInt64(Math.Round((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds, MidpointRounding.AwayFromZero));
        }

        public static string SubstitueCommandTokens(JObject check, out string errors, JObject client)
        {
            errors = "";
            var tempErrors = new List<string>();
            var command = check["command"].ToString();

            if (!command.Contains(":::"))
                return command;

            var commandRegexp = new Regex(":::(.*?):::", RegexOptions.Compiled);

            command = commandRegexp.Replace(command, match => MatchCommandArguments(client, match, tempErrors));

            if (tempErrors.Count > 0)
            {
                errors = ErroTextDefaultValueMissing;
                errors += string.Join(" , ", tempErrors.ToArray());
            }
            return command;
        }

        private static string MatchCommandArguments(JObject client, Match match, List<string> tempErrors)
        {
            var argumentValue = "";
            string[] commandArgument;

            if (match.Value.Contains("|"))
            {
                commandArgument = match.Value.Replace(":::", "").Split(('|'));
            }
            else
            {
                commandArgument = new[] { match.Value.Replace(":::", "").ToString(), ""};
            }

            var matchedOrDefault = FindClientAttribute(client, commandArgument[0].Split('.').ToList(), commandArgument[1]);

            if (CommandArgumentIsNotNull(commandArgument)) {
                argumentValue += matchedOrDefault;
            }

            if (String.IsNullOrWhiteSpace(argumentValue))
            {
                tempErrors.Add(commandArgument[0]);
            }

            return argumentValue;
        }

        private static bool CommandArgumentIsNotNull(string[] p)
        {
            return p[0] != null;
        }

        private static string FindClientAttribute(JToken tree, ICollection<string> path, string defaultValue)
        {
            var attribute = tree[path.First()];
            path.Remove(path.First());
            if (attribute == null) return defaultValue;
            if (attribute.Children().Any())
            {
                return FindClientAttribute(attribute, path, defaultValue);
            }

            return attribute.Value<string>() ?? defaultValue;

        }

        //public static string SubstitueCommandTokens(JObject check,JObject m_configsettings, out List<string> unmatchedTokens)
        //{
        //    var temptokens = new List<string>();
        //    var command = check["command"].ToString();
        //    var regex = new Regex(":::(.*?):::", RegexOptions.Compiled);
        //    command = regex.Replace(command, match =>
        //    {
        //        var matched = "";
        //        foreach (var p in match.Value.Split('.'))
        //        {

        //            Log.Debug("Searching for token {0} in client config", p);

        //            if (m_configsettings["client"][p] != null)
        //            {
        //                matched += m_configsettings["client"][p];
        //            }
        //            else
        //            {
        //                break;
        //            }
        //        }

        //        if (string.IsNullOrEmpty(matched))
        //        {

        //            if (match.Value.Contains("|")) //something like  this: :::cpu.usage|80:::
        //            {

        //                matched += match.Value.Remove(0, match.Value.IndexOf("|") + 1).ToString().Replace(":::", "").Trim();
        //            }
        //            else
        //            {
        //                temptokens.Add(match.Value);
        //            }

        //        }

        //        return matched;
        //    });

        //    unmatchedTokens = temptokens;

        //    return command;
        //}

        public static List<string> GetRedactlist(JObject check)
        {
            List<string> redactlist = null;

            if ((check["client"] == null || check["client"]["redact"] == null)) return redactlist;

            var redact = check["client"]["redact"];

            if (redact == null || string.IsNullOrEmpty(redact.ToString())) return redactlist;

            var redactArray = redact.ToString().Split(' ');
            redactlist = new List<string>(redactArray);

            return redactlist;
        }

        private static JToken RedactInformationRecursive(JToken check, List<string> keys = null)
        {

            if (keys == null) keys = new List<string>(){"password", "passwd",
                                                        "pass","api_key","api_token","access_key",
                                                        "secret_key", "private_key","secret"};

            if (check.Children().Any())
            {
                foreach (var child in check.Children())
                {

                    RedactInformationRecursive(child, keys);

                    if (child.Type == JTokenType.Property)
                    {
                        var property = child as Newtonsoft.Json.Linq.JProperty;

                        if (keys.Contains(property.Name))
                        {
                            property.Value = "REDACTED";
                        }
                    }

                }
            }
            return check;
        }

        public static JToken RedactSensitiveInformaton(JToken check, List<string> keys = null)
        {
            var clonedCheck = check.DeepClone();

            return RedactInformationRecursive(clonedCheck, keys);
        }

    }
}
