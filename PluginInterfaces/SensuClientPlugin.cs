using System.Collections.Generic;

namespace sensu_client.net.pluginterface
{
    public interface ISensuClientPlugin
    {
        string GUID();
        string Name();
        string Version();
        string Author();

        void Initialize();
        List<string> Handlers();

        string execute(string handler, params KeyValuePair<string,string>[] arguments);

    }
}
