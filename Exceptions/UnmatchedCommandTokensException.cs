using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net.Exceptions
{
    class UnmatchedCommandTokensException: Exception
    {
        public UnmatchedCommandTokensException()
        {

        }

        public UnmatchedCommandTokensException(string message)
            : base(message)
        {

        }

    }
}
