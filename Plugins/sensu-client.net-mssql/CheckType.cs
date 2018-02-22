using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net_mssql
{

    public enum CheckType : int
    {
        AvailbilityGroup = 0,
        AvailbilityGroupDatabases = 1
    }

}
