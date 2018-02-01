using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net
{
    class CheckInProgressException: Exception 
    {

        public CheckInProgressException()
        {

        }

        public CheckInProgressException(string message)
            : base(message)
        {

        }
    }
}
