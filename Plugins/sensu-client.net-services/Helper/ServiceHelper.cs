using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net_services
{
    public static class ServiceHelper
    {

        public static StartMode GetStartMode(string m_servicename)
        {

            StartMode m_startmode = StartMode.Undefinied;
            RegistryKey m_reg = null;
            int m_start_value = 0;
            int m_delayed_start_value = 0;

            try
            {

                m_reg = Registry.LocalMachine.OpenSubKey(String.Format("SYSTEM\\CurrentControlSet\\Services\\{0}", m_servicename), false);

                if (m_reg != null)
                {

                    m_start_value = Convert.ToInt32(m_reg.GetValue("Start", -1));
                    m_delayed_start_value = Convert.ToInt32(m_reg.GetValue("DelayedAutoStart", 0));

                    if(m_start_value == -1)
                    {
                        throw new Exception(String.Format("Registry value 'Start' not found for service {0}", m_servicename));
                    }
                    else
                    {
                        m_startmode = (StartMode)Enum.Parse(typeof(StartMode),(m_start_value - m_delayed_start_value).ToString());
                    }

                }
                else
                {
                    throw new ArgumentException("Any service found with the specified name");
                }

            }
            catch
            {
                m_startmode = StartMode.Undefinied;
            }
            finally
            {
                if(m_reg !=null)
                {
                    m_reg.Dispose();
                }
            }


            return m_startmode;

        }

    }

}
