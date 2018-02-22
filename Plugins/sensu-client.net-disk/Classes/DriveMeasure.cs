using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sensu_client.net_disk
{

    public class DriveMeasure

    {
        public enum State
        {
            OK, WARNING, CRITICAL, UNKNOWN
        }

        public State DriveState;

        public string ID;

        public double FreeSpace;

        public double Size;

        public double UsedPercentage;

        public override string ToString()
        {
            return String.Format("({0}) {1}%, FREE: {2} GB, SIZE: {3} GB`n", this.ID, this.UsedPercentage, DriveMeasureExtension.ToSize(this.FreeSpace, DriveMeasureExtension.SizeUnits.GB), DriveMeasureExtension.ToSize(this.Size, DriveMeasureExtension.SizeUnits.GB));
        }

    }

}
