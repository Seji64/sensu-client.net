using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net_services
{
    public enum StartMode : int
    {
        Undefinied = 0,
        AutomaticDelayed = 1,
        Automatic = 2,
        Manual = 3,
        Disabled = 4
    }
}
