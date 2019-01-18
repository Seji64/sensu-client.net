using sensu_client.net.pluginterface;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net.plugin
{
    [Export(typeof(ISensuClientPlugin))]
    public class posh : ISensuClientPlugin
    {

        private RunspacePool m_posh_runspace_pool = null;
    
        public string Author()
        {
            return "Tim Hofmann";
        }

        public void Dispose()
        {
            if (m_posh_runspace_pool != null && m_posh_runspace_pool.IsDisposed == false)
            {

                m_posh_runspace_pool.Dispose();

            }
        }

        public ExecuteResult execute(string handler, Arguments arguments)
        {

            switch (handler)
            {

                case "!posh":

                    ExecuteResult m_result = new ExecuteResult();
                    PowerShell m_posh = null;
                    Command m_posh_command = null;
                    Collection<PSObject> m_posh_object_output = null;
                    StringBuilder m_posh_string_output = new StringBuilder();
                    StringBuilder m_posh_error = new StringBuilder();

                    string m_ps_command = string.Empty;
                    

                    if ((arguments.Exists("psscript") || arguments.Exists("pscommand")))
                    {

                        if (arguments.Exists("psscript"))
                        {
                            m_ps_command = Environment.ExpandEnvironmentVariables(arguments.Single("psscript"));

                            if (System.IO.File.Exists(m_ps_command) == false)
                            {
                                throw new System.IO.FileNotFoundException("PowerScript not found!");
                            }

                        }
                        else if (arguments.Exists("pscommand"))
                        {
                            m_ps_command = arguments.Single("pscommand");

                        }

                        if (string.IsNullOrWhiteSpace(m_ps_command))
                        {
                            throw new Exception("Script or Command Argument has to be defined!");
                        }

                    }
                    else
                    {
                        throw new ArgumentException("Argument 'powershell script' (--psscript) OR 'powerhell command' (--pscommand) is mandatory!");
                    }


                    try
                    {

                        m_posh = PowerShell.Create();
                        m_posh.RunspacePool = m_posh_runspace_pool;

                        #region "Set Command and add arguments"

                        m_posh_command = new Command(m_ps_command);

                        foreach (var arg in arguments.ParsedArguments)
                        {

                            if (arg.Key != null && arg.Key != "psscript" & arg.Key != "pscommand")
                            {

                                if (arg.Value != null)
                                {

                                    if (arg.Value.Count == 1)
                                    {

                                        if (arg.Value[0].ToString().ToLower() == "true" || arg.Value[0].ToString().ToLower() == "false")
                                        {
                                            m_posh_command.Parameters.Add(arg.Key, bool.Parse(arg.Value[0].ToString()));
                                        }
                                        else
                                        {
                                            m_posh_command.Parameters.Add(arg.Key, arg.Value[0]);
                                        }
                        
                                    }
                                    else
                                    {
                                        throw new Exception("More than 1 Value is currently not supported");
                                    }
                                   
                                }
                                else
                                {
                                    throw new Exception("Key Value cannot be null!");

                                }

                            }

                        }

                        #endregion

                        //Fire Powershell
                        m_posh.Commands.AddCommand(m_posh_command);
                        m_posh_object_output = m_posh.Invoke();

                        #region "Check for errors and throw it when present"

                        if (m_posh.Streams.Error != null && m_posh.Streams.Error.Count > 0)
                        {
                            foreach(var error in m_posh.Streams.Error)
                            {
                                m_posh_error.AppendLine(error.ToString());
                            }

                            throw new Exception(string.Format("Powershell Error => {0}", m_posh_error.ToString()));
                        }

                        #endregion

                        if (m_posh_object_output != null && m_posh_object_output.Count != 0)
                        {

                            foreach (var output_line in m_posh_object_output)
                            {
                                m_posh_string_output.AppendLine(output_line.ToString());
                            }

                        }
                        else if (m_posh.Streams.Information != null && m_posh.Streams.Information.Count > 0)
                        {
                            foreach (var output_line in m_posh.Streams.Information)
                            {
                                m_posh_string_output.AppendLine(output_line.ToString());
                            }
                        }

                        m_result.ExitCode = 0;
                        m_result.Output = m_posh_string_output.ToString();

                        #region "Cleanup"
                        if (m_posh != null)
                        {
                            m_posh.Dispose();
                        }
                        #endregion

                    }
                    catch (Exception ex)
                    {
                        #region "Cleanup"
                        if (m_posh != null)
                        {
                            m_posh.Dispose();
                        }
                        #endregion
                        throw new Exception(string.Format ("failed to execute PowerShell Command/Script => {0}",ex.Message));
                    }

                    return m_result;

                default:

                    throw new Exception("Malformed check command!");

            }

    }

        public string GUID()
        {
            return "39912EB7-F27E-4A77-81E8-B19A1E8CFD87";
        }

        public List<string> Handlers()
        {
            List<string> m_handlers = new List<string>();

            m_handlers.Add("!posh");

            return m_handlers;
        }

        public void Initialize()
        {
            m_posh_runspace_pool = RunspaceFactory.CreateRunspacePool(5, 25);
            m_posh_runspace_pool.ThreadOptions = PSThreadOptions.Default;
            m_posh_runspace_pool.Open();

        }

        public string Name()
        {
            return "PowerShellExecPlugin";
        }

        public string Version()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }
    }
}
