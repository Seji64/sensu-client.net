using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net.Exceptions
{
    class InvalidPluginException: Exception
    {
        public InvalidPluginException()
        {

        }

        public InvalidPluginException(string message)
            :base(message)
        {

        }
    }
}
