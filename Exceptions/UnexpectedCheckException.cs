using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net.Exceptions
{
    class UnexpectedCheckException: Exception
    {
        public UnexpectedCheckException()
        {

        }

        public UnexpectedCheckException(string message)
            : base(message)
        {

        }

    }
}
