using System.Collections.Generic;

namespace sensu_client.net.pluginterface
{
    public interface ISensuClientPlugin
    {
        string GUID();
        string Name();
        string Version();
        string Author();

        void Initialize();
        List<string> Handlers();

        ExecuteResult execute(string handler, Arguments arguments);

    }

    public class ExecuteResult
    {

        private string m_output;
        public string Output
        {
            get
            {
                return this.m_output;
            }
            set
            {
                m_output = value;
            }
        }

        private int m_exitcode;
        public int ExitCode
        {
            get
            {
                return this.m_exitcode;
            }
            set
            {
                m_exitcode = value;
            }
        }
        
    }
}
