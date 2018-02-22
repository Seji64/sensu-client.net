using System;
using System.Collections.Generic;

namespace sensu_client.net.pluginterface
{
    public interface ISensuClientPlugin : IDisposable

    {
        string GUID();
        string Name();
        string Version();
        string Author();

        void Initialize();
        List<string> Handlers();

        ExecuteResult execute(string handler, Arguments arguments);
      
    }
}
