using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace sensu_client.net.pluginterface
{
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
