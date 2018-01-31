using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApplication1
{
    class Cluster
    {
        public string[] attributes { get; private set; }
        public int id { get; private set; }
        public int type { get; private set; }
        public int centerX { get; private set; }
        public int centerY { get; private set; }
        public int height { get; private set; }
        public string[] vertices { get; private set; }

        // Constructor
        public Cluster(string[] attributes, int id, int type, int x, int y, int height)
        {
            this.attributes = attributes;
            this.id = id;
            this.type = type;
            this.centerX = x;
            this.centerY = y;
            this.height = height;
        }

        // Method to output data
        public string stringify()
        {
            return id + ";" + type + ";(" + centerX + "|" + centerY + ");" + height;
        }
    }
}
