using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net_mssql
{

    public enum MetricType : int
    {
        DatabaseIO = 0,
        ServerProperties = 1,
        PerformanceCounter = 2,
        PerformanceMetrics = 3,
        WaitStats = 4,
        MemoryClerk = 5,
        DatabaseSize = 6
        
    }

}
