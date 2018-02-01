using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net.Exceptions
{
    class EmptyCheckNameException: Exception
    {
        public EmptyCheckNameException()
        {

        }

        public EmptyCheckNameException(string message)
            :base(message)
        {

        }

    }
}
