using System;
using System.Threading;

namespace DAMReaderExample
{
    class Program
    {
        static void Main(string[] args)
        {
            // Create an instance of the DAMReader class
            DAMReader damReader = new DAMReader("COM1", 5);

            // Check if the DAMReader is connected
            if (damReader.Connected)
            {
                Console.WriteLine("DAMReader is connected. Press Ctrl+C to stop.");

                // Continuously read and display values
                while (true)
                {
                    // Access the desired properties
                    int knockLeft = damReader.KnockLeft;
                    int knockRight = damReader.KnockRight;
                    double rpm = damReader.GetRPM(ref rpm); // Assuming you have a ref double variable for RPM
                    int[] cylKnockCounts = damReader.CylinderValues;

                    // Print the values
                    Console.WriteLine($"Knock Left: {knockLeft}");
                    Console.WriteLine($"Knock Right: {knockRight}");
                    Console.WriteLine($"RPM: {rpm}");
                    Console.WriteLine("Cylinder Knock Counts:");
                    for (int i = 0; i < cylKnockCounts.Length; i++)
                    {
                        Console.WriteLine($"Cylinder {i + 1}: {cylKnockCounts[i]}");
                    }

                    // Wait for a short period before reading again
                    Thread.Sleep(5);
                }
            }
            else
            {
                Console.WriteLine("DAMReader is not connected.");
            }

            // Close the DAMReader when done
            damReader.Close();
        }
    }
}
