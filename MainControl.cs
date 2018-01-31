using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Exceptions;
using uPLibrary.Networking.M2Mqtt.Messages;
using uPLibrary.Networking.M2Mqtt.Utility;

namespace ConsoleApplication1
{
    class MainControl
    {
        private static MqttClient client;
        private static byte successCode;
        private static List<Cluster> clustersBB1;
        private static List<Objects> usableObjects;
        private static string subscribedTopicBB1;
        private static string subscribedTopicCrane;
        private static int humanType;  // Type in MQTT strings to listen
        private static int humanController;  // ID of human controller,
        private static bool humanControlEnabled = false;
        private static double craneTravelDistance;  // Distance between crane's end positions
        private static int targetPositionYLast = 0;  // Needed to smooth driving data
        private static int targetPositionZLast = 0;  // Needed to smooth lifting data
        private static int curPositionX;
        private static int curPositionZ;
        private static int liftLength = 2600;
        private static int failCounter = 0;  // Counts number of times controlHuman was not found
        private static int lostCounter = 0;  // Counts how often human control was lost
        private static String timeStamp;  // Use to store time the control was activated

        static void Main(string[] args)
        {
            /* ------------------------------------------
             * Setup MQTT-Connection
             * ------------------------------------------
             */
            Console.WriteLine("Start...");
            client = new MqttClient("172.22.222.31");
            successCode = client.Connect("GestureControl");

            subscribedTopicCrane = "Crane/SafetyCrane/Data1";
            subscribe(subscribedTopicCrane, 2);
            subscribedTopicBB1 = "blackboard/bb2/cluster";
            subscribe(subscribedTopicBB1, 2);
            //subscribe("Crane/SafetyCrane/Control", 2);
            client.MqttMsgPublishReceived += client_MqttMsgPublishReceived;

            /* ------------------------------------------
             * Setup usable-objects list
             * ------------------------------------------
             */
            usableObjects = new List<Objects>();
            usableObjects.Add(new Objects("SafetyCraneLeftEnd", 0, 0));
            usableObjects.Add(new Objects("SafetyCraneRightEnd", 0, 400));

            Console.WriteLine("Usable objects:");
            for (int i = 0; i < usableObjects.Count; i++)
            {
                Console.WriteLine(usableObjects[i].stringify());
            }


            /* ------------------------------------------
             * Setup cluster-list and search for human
             * ------------------------------------------
             */
            clustersBB1 = new List<Cluster>();
            humanType = 3;
            humanController = 0;

            while (true)
            {
                //publish(publishString, 2, "Crane/SafetyCrane/Control");
                // Print cluster on command
                if (Console.ReadKey().Key == ConsoleKey.B)
                {
                    publish("Magnet", 2, "Crane/SafetyCrane/Control");
                    publish("1000;1000;1000;1000", 2, "Crane/SafetyCrane/Control");

                    Console.WriteLine();
                    for (int i = 0; i < clustersBB1.Count; i++)
                    {
                        Console.WriteLine(clustersBB1[i].stringify());
                    }
                }
            }
        }



        /* ------------------------------------------
         * Functional methods
         * ------------------------------------------
         */

        // Human controls crane
        private static void humanControl()
        {
            int targetPositionZ;
            int targetPositionY;
            int eps = 70;  // Smoothing: Difference of target positions
            string publishString;
            bool personFound = false;

            // Find cluster with humanController ID
            for (int n = 0; n < clustersBB1.Count; n++)
            {
                if (clustersBB1[n].id == humanController)
                {
                    targetPositionY = Convert.ToInt32(1000 - (clustersBB1[n].centerY / craneTravelDistance * 1000));
                    
                    // Difference too small - don't go to this position
                    if (Math.Abs(targetPositionY - targetPositionYLast) < eps)
                    {
                        targetPositionY = targetPositionYLast;
                    }
                    else
                    {
                        targetPositionYLast = targetPositionY;
                    }

                    // Lift if user is nearby
                    if(clustersBB1[n].centerX < 240)
                    {
                        targetPositionZ = Convert.ToInt32((clustersBB1[n].height - (clustersBB1[n].height / 4.0)) / liftLength * 1000.0);

                        if(Math.Abs(targetPositionZ - targetPositionZLast) < eps)
                        {
                            targetPositionZ = targetPositionZLast;
                        }
                        else
                        {
                            targetPositionZLast = targetPositionZ;
                        }
                    }
                    else
                    {
                        targetPositionZ = 1000;
                    }
                    publishString = targetPositionY + ";" + targetPositionZ + ";0000;1000";
                    Console.WriteLine("Distance = " + clustersBB1[n].centerX);
                    Console.WriteLine("Publish: " + publishString);
                    Console.WriteLine("x = " + clustersBB1[n].centerX);
                    Console.WriteLine("y = " + clustersBB1[n].centerY);
                    publish(publishString, 2, "Crane/SafetyCrane/Control");
                    personFound = true;
                }
            }
            if(!personFound)
            {
                failCounter++;

                if (failCounter >= 20)
                {
                    lostCounter++;
                    Console.WriteLine("Lost human control! " + lostCounter);
                    failCounter = 0;
                    humanControlEnabled = false;
                    humanController = 0;

                    // Stop at current position
                    publish(stopString(curPositionX, curPositionZ), 2, "Crane/SafetyCrane/Control");
                }
            }
        }

        // Method to search cluster for human (type == humanType := 3)
        private static void searchCluster()
        {
            // Search for human
            double[] line = new double[] { 0.0, 0.0 };
            for (int i = 0; i < clustersBB1.Count; i++)
            {
                if (clustersBB1[i].type == humanType)
                {
                    // Only search for human if no human is in control
                    if (humanController == 0)
                    {
                        if (isPointing(clustersBB1[i].centerX, clustersBB1[i].centerY, clustersBB1[i].attributes))
                        {
                            //Console.WriteLine("--- Pointing human detected ---");
                            int[] shapePoint = getShapePoint(clustersBB1[i].centerX, clustersBB1[i].centerY, clustersBB1[i].attributes);
                            line = pointingVector(clustersBB1[i].centerX, clustersBB1[i].centerY, shapePoint);

                            for (int j = 0; j < usableObjects.Count; j++)
                            {
                                bool direction = getDistance(shapePoint[0], shapePoint[1],
                                               usableObjects[j].centerX, usableObjects[j].centerY) <
                                   getDistance(clustersBB1[i].centerX, clustersBB1[i].centerY,
                                               usableObjects[j].centerX, usableObjects[j].centerY);
                                bool withinSpread = pointInSpread(usableObjects[j].centerX,
                                                                  usableObjects[j].centerY,
                                                                  clustersBB1[i].centerX,
                                                                  clustersBB1[i].centerY,
                                                                  line);

                                // Activation of crane
                                if (!humanControlEnabled && direction && withinSpread && line[0] != 999.0 && line[1] != 999.0)
                                {
                                    Console.WriteLine("Right direction? - True");
                                    Console.WriteLine("Point in spread? - True");
                                    // Activate control for this pointing human and set time stamp
                                    humanController = clustersBB1[i].id;
                                    humanControlEnabled = true;
                                    publish("Magnet", 2, "Crane/SafetyCrane/Control");
                                    craneTravelDistance = getDistance(usableObjects[0].centerX,
                                                                      usableObjects[0].centerY,
                                                                      usableObjects[1].centerX,
                                                                      usableObjects[1].centerY);
                                    Console.WriteLine("CraneTravelDst = " + craneTravelDistance);
                                    Console.WriteLine("ID = " + humanController);
                                    //timeStamp = GetTimestamp(DateTime.Now);
                                }
                            }
                        }
                    }

                    else
                    {
                        line[0] = 999.0;
                        line[1] = 999.0;
                    }
                }

                else
                {
                    //Console.WriteLine("No human detected");
                }
            }
        }

        // Method to parse cluster-data
        private static void parseCluster(int id, string msg)
        {
            clustersBB1.Clear();

            string[] lines = msg.Split(new string[] { "\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                string[] attributes = line.Split(';');
                string[] type = attributes[1].Split(':');
                string[] center = attributes[2].Split('|');     // center point

                clustersBB1.Add(new Cluster(
                    attributes,                         // all attributes
                    Convert.ToInt32(attributes[0]),     // ID
                    Convert.ToInt32(type[0]),           // type
                    Convert.ToInt32(center[0]),         // centerX
                    Convert.ToInt32(center[1]),         // centerY
                    Convert.ToInt32(attributes[4])));   // height
            }
        }

        // Get current crane-positions
        private static void getPositions(string msg)
        {
            string[] attributes = msg.Split(';');
            curPositionX = Convert.ToInt32(attributes[0]);
            curPositionZ = Convert.ToInt32(attributes[1]);

            //if(humanControlEnabled)
            //{
            //    Console.WriteLine(curPositionX + "/" + curPositionZ);
            //}
        }

        // Method to create "stop string"
        private static string stopString(int x, int z)
        {
            string result = "";

            // Check x-values
            if (x < 10)
            {
                result += "000" + x;
            }
            else if (x < 100)
            {
                result += "00" + x;
            }
            else if (x < 1000)
            {
                result += "0" + x;
            }
            else
            {
                result += x;
            }

            result += ";";

            // Check z-values
            if (z < 10)
            {
                result += "000" + z;
            }
            else if (z < 100)
            {
                result += "00" + z;
            }
            else if (z < 1000)
            {
                result += "0" + z;
            }
            else
            {
                result += z;
            }

            // Append target values for second chain hoist
            result += ";0000;1000";
            Console.WriteLine(x + " " + z + " " + result);

            return result;
        }


        /* ------------------------------------------
         * MQTT relevant methods
         * ------------------------------------------
         */

        // Method to subscribe to topic
        public static void subscribe(string topic, byte qos)
        {
            if (successCode == 0)
            {
                client.MqttMsgSubscribed += client_MqttMsgSubscribed;
                Console.WriteLine("Verbunden");
                ushort msgId = client.Subscribe(new string[] { "/" + topic }, new byte[] { qos });
                Console.WriteLine("Subsribed: " + topic);
            }
        }

        // Method to publish on topic
        public static void publish(String message, byte qos, string topic)
        {
            if (successCode == 0)
            {
                client.MqttMsgPublished += client_MqttMsgPublished;
                // asyncronous but msgId gets defined instantly
                ushort msgId = client.Publish("/" + topic, Encoding.UTF8.GetBytes(message),
                              qos, false);
            }
        }

        // Called if a message was received on a subsribed topic
        public static void client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            // Message received on topic "BB1"
            if (e.Topic.EndsWith(subscribedTopicBB1))
            {
                //Console.WriteLine();
                parseCluster(1, Encoding.UTF8.GetString(e.Message));

                if(!humanControlEnabled)
                {
                    searchCluster();  // Search cluster list for human
                }
                else
                {
                    humanControl();  // Human controls crane
                }
            }

            // Message received on topic "Crane"
            if (e.Topic.EndsWith(subscribedTopicCrane))
            {
                getPositions(Encoding.UTF8.GetString(e.Message));
            }
        }

        // Called if the message was successfully published or if it failed after serveral times (only for Qos 1 or 2 of course)
        private static void client_MqttMsgPublished(object sender, MqttMsgPublishedEventArgs e)
        {
            //Console.WriteLine("MessageId = " + e.MessageId + " Published = " + e.IsPublished);
        }

        // Called if the topic was successfully subscribed
        private static void client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            //Console.WriteLine("Subscribed for id = " + e.MessageId);
        }


        /* -------------------------------------------
         * Usable objects related methods
         * ------------------------------------------
         */

        // Is pointing: Make sure, human is actually pointing
        private static bool isPointing(int centerX, int centerY, string[] attributes)
        {
            double eps = 0.1;
            double factor = 0.035;
            double proportionLowerBound = 0.9;  // Approx. lower bound for proportion of size / arm length
            double proportionUpperBound = 1.35;  // Approx. upper bound for proportion of size / arm length
            double height = Convert.ToInt32(attributes[4]) * factor;
            double dstcSholderHead = height * 3.0 / 8.0;
            int[] shapePoint = getShapePoint(centerX, centerY, attributes);
            double distance = getDistance(shapePoint[0], shapePoint[1], centerX, centerY);
            double distanceScnd = getDistance(shapePoint[2], shapePoint[2], centerX, centerY);

            // Make sure second farest shape point is significantly nearer to center than farest shape point
            Console.WriteLine("Distance = " + distance + " Height = " + height);
            Console.WriteLine("Dstc/Height = " + (distance / height));

            // proportion must be bigger than underBound and lower than upper bound. No control for sitting human
            return ((distance / height) < proportionUpperBound && (distance / height) > proportionLowerBound && ((height / factor) >= 1500));
        }

        // Get shape-point with highest distance to center
        private static int[] getShapePoint(int centerX, int centerY, string[] attributes)
        {
            int[] shapePoint = new int[] { 0, 0, 0, 0 };
            double dstc;
            double dstc_highest = 0.0;
            double dstc_scnd = 0.0;

            if (attributes.Length >= 9)
            // Object has at least one shape-point
            {
                for (int i = 8; i < attributes.Length; i++)
                {
                    try
                    {
                        string[] coordinates = attributes[i].Split('|');
                        int x = Convert.ToInt32(coordinates[0]);
                        int y = Convert.ToInt32(coordinates[1]);
                        dstc = getDistance(x, y, centerX, centerY);

                        if (dstc > dstc_highest)
                        {
                            dstc_highest = dstc;
                            shapePoint[0] = x;  // Shape-point x
                            shapePoint[1] = y;  // Shape-point y
                        }
                        if (dstc > dstc_scnd && dstc < dstc_highest)
                        {
                            dstc_scnd = dstc;
                            shapePoint[2] = x;  // Second farest shape-point x
                            shapePoint[3] = y;  // Second farest shape-point y
                        }
                    }
                    catch (Exception)
                    {
                        //Console.WriteLine("Exception while computing shape-point");
                    }
                }
            }
            else
            {
                Console.WriteLine("Unable to compute Shape Point");
            }

            return shapePoint;
        }

        // Compute pointing vector
        private static double[] pointingVector(int centerX, int centerY, int[] shapePoint)
        {
            double[] lineEq = new double[] { 0.0, 0.0 };

            // Compute linear equation between center and shape-point
            if(shapePoint[0] != centerX)
            {
                lineEq[0] = ((shapePoint[1] - centerY) * 1.0 / (shapePoint[0] - centerX));  // m
                lineEq[1] = centerY - (1.0 * centerX * lineEq[0]);  // b
            }
            else
            {
                lineEq[0] = centerX;
                lineEq[1] = 9999.0;
            }

            if(!(pointOnLine(shapePoint[0], shapePoint[1], lineEq) && pointOnLine(centerX, centerY, lineEq)))
            {
                Console.WriteLine("Unable to compute pointing vector");                
            }

            return lineEq;
        }

        // Compute euclidean distance between two points (+1 Overload)
        private static double getDistance(int x1, int y1, int x2, int y2)
        {
            return Math.Sqrt(((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2)));
        }
        private static double getDistance(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt(((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2)));
        }

        // Compute spreading
        private static double spreading(double x)
        {
            double alpha = 10.0;
            return Math.Abs(Math.Sin(alpha) * x);
        }

        // Compute intersection point between two lines
        private static double intersection(double m1, double b1, double m2, double b2)
        {
            return ((b1 - b2) / (m2 - m1));
        }

        // Check if given point lies on pointing vector
        private static bool pointOnLine(int x, int y, double[] line)
        {
            // line[0] = m
            // line[1] = b
            double epsilon = 0.01;  // Detect failure

            if(line[1] != 9999.0)
            {
                return (Math.Abs((x * line[0]) + line[1] - y) < epsilon);
            }

            else
            {
                // No gradient in line => shapeX = centerX
                return ((x - line[0]) < epsilon);
            }
        }

        // Check if given point lies within spread
        private static bool pointInSpread(int targetX, int targetY, int centerX, int centerY, double[] line)
        {
            // line[0] = m
            // line[1] = b
            double epsilon = 0.01;  // Detect failure
            double spread;          // Refines required sharpness of pointing direction
            double m_sup;  // m of support line
            double b_sup;  // b of support line
            double intersectionX;  // x-coordinate of intersection point
            double intersectionY;  // y-coordinate of intersection point
            double distance;
            double distance_spread;

            // (1) Create support line orthogonal to pointing direction which goes through (targetX|targetY)
            m_sup = -1.0 / line[0];
            b_sup = targetY - m_sup * targetX;

            intersectionX = intersection(line[0], line[1], m_sup, b_sup);
            intersectionY = m_sup * intersectionX + b_sup;

            if (Math.Abs((m_sup * intersectionX + b_sup) - (line[0] * intersectionX + line[1])) > epsilon)
            {
                // Intersection point is not equal to both equations
                Console.WriteLine("Failed to create support line");
            }

            // (2) Compute distance between intersection point and target
            spread = getDistance(centerX, centerY, intersectionX, intersectionY);
            distance = getDistance(intersectionX, intersectionY, targetX, targetY);
            distance_spread = spreading(spread);

            //Console.WriteLine("Distance = " + distance + " Spread = " + distance_max);

            return (distance < distance_spread);
        }
    }
}
