using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApplication1
{
    class Objects
    // All objects to point at
    {
        public string name { get; private set; }
        public int centerX { get; private set; }
        public int centerY { get; private set; }

        // Constructor
        public Objects(string name, int x, int y)
        {
            this.name = name;
            this.centerX = x;
            this.centerY = y;
        }

        // Print
        public string stringify()
        {
            return (this.name + " at: (" + this.centerX + "|" + centerY + ")");
        }
    }
}
