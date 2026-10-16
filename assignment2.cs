using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Tmds.DBus.Protocol;


namespace ImageApp
{
    public partial class MainWindow : Window
    {

        public class Assignment2{

            //here comes the sunshine

            //for sequential region labeling several steps need to be done
            //

            public byte[,] SequentialRegionLabeling(byte[,] inputImage)
            {
                //to do
                //something is wrong with the labelling the colours are darker than if i use the floodfill function
                //which means something is wrong
                int iW = inputImage.GetLength(0);
                int iH = inputImage.GetLength(1);

                (int i, int j)[] neighbours =
                {
                    (-1, -1),
                    (-1, 0),
                    (-1, 1),
                    (0, -1)
                };

                byte[,] labeledImage = new byte [iW, iH];

                //The label value that keeps track of which label value we are on
                byte label = 1;

                //First pass
                for (int u = 0; u < iW; u++)
                for (int v = 0; v < iH; v++)
                {
                    if (inputImage[u, v] == 255)
                    {
                        //fix this this is a tempvalue
                        byte tempLabel = 255;
                        //Checks the neighbouring pixels
                        foreach ((int i, int j) in neighbours)
                        {
                            int uOffset = Math.Clamp(u + i, 0, iW - 1);
                            int vOffset = Math.Clamp(v + j, 0, iH - 1);
                            byte labelVal = labeledImage[uOffset, vOffset];

                            //Sees if there is a lower label value in one of the neighbouring pixels
                            if (labelVal > 0 && labelVal < tempLabel)
                            {
                                tempLabel = labelVal;
                            }
                        }

                        //If no neighbouring label was found create a new label
                        if (tempLabel == 255)
                        {
                            labeledImage[u, v] = label;
                            label++;
                            //not above 254 values but eh (for now)
                        }
                        else
                        {
                            labeledImage[u, v] = tempLabel;
                        }
                    }
                }

                labeledImage = ResolveCollisions(labeledImage, iW, iH);

                //now we need to do contrast adjustment

                return labeledImage;
            }

            private byte[,] ResolveCollisions(byte[,] labeledImage, int iW, int iH)
            {
                //Second pass this needs to go a bit differently
                //because we need to find all the labels that are connected
                Dictionary<byte, byte> labelSets = new Dictionary<byte, byte>();

                for (int u = 0; u < iW; u++)
                for (int v = 0; v < iH; v++)
                {
                    if (labeledImage[u, v] > 0)
                    {
                        for (int i = -1; i <= 1; i++)
                        for (int j = -1; j <= 1; j++)
                        {
                            int uOffset = Math.Clamp(u + i, 0, iW - 1);
                            int vOffset = Math.Clamp(v + j, 0, iH - 1);

                            byte neighbour = labeledImage[uOffset, vOffset];
                            byte current = labeledImage[u, v];
                            if (current != neighbour && neighbour > 0)
                            {
                                labelSets = Union(labelSets, neighbour, current);
                            }
                        }
                    }
                }

                //update the labeled image
                for (int u = 0; u < iW; u++)
                for (int v = 0; v < iH; v++)
                {
                    if (labelSets.ContainsKey(labeledImage[u, v]))
                    {
                        labeledImage[u, v] = Find(labeledImage[u, v], labelSets);
                    }
                }

                return labeledImage;
            }

            private byte Find(byte x, Dictionary<byte, byte> labelSets)
            {
                if (labelSets[x] != x)
                {
                    labelSets[x] = Find(labelSets[x], labelSets);
                }

                return labelSets[x];
            }

            private Dictionary<byte, byte> Union(Dictionary<byte, byte> labelSets, byte a, byte b)
            {
                //https://stackoverflow.com/questions/46383172/disjoint-set-implementation-in-c-sharp
                //https://www.geeksforgeeks.org/dsa/introduction-to-disjoint-set-data-structure-or-union-find-algorithm/

                if (!labelSets.ContainsKey(a))
                {
                    labelSets[a] = a;
                }

                if (!labelSets.ContainsKey(b))
                {
                    labelSets[b] = b;
                }

                byte rootA = Find(a, labelSets);
                byte rootB = Find(b, labelSets);

                if (rootA == rootB)
                {
                    return labelSets;
                }

                if (rootA < rootB)
                {
                    labelSets[rootB] = rootA;
                }
                else
                {
                    labelSets[rootA] = rootB;
                }

                return labelSets;
            }
            
            public static byte[,] HoughTransform(byte[,] inputImage, int isBinary = 0)
            {
                int iW = inputImage.GetLength(0);
                int iH = inputImage.GetLength(1);
                
                //center of image
                int uCenter = iW / 2;
                int vCenter = iH / 2;
                
                //max radius
                int rMax = (int)Math.Sqrt(uCenter * uCenter + vCenter * vCenter);
                
                //angular increment
                const int angularSteps = 256;
                double angularStepSize = MathF.PI / angularSteps;
                
                //radial increment
                const int radialSteps = 256;
                int radiusCenter = radialSteps / 2;
                double radialStepSize = (2.0 * rMax)/radialSteps;
                
                //create accumulator and set cells to 0
                int[,] accumulator = new int[angularSteps, radialSteps];
                int maxLine = 0;
                
                //scan image
                for (int u = 0; u < iW; u++)
                {
                    for (int v = 0; v < iH; v++)
                    {
                        
                        if (inputImage[u, v] > 0)
                        {
                            int weight;
                            if (isBinary == 1)
                                weight = 1;
                            else
                                weight = inputImage[u, v];    
                            
                            int x = u - uCenter;
                            int y = v - vCenter;
                            
                            for (int angI = 0; angI < angularSteps; angI++)
                            {
                                double theta = angularStepSize * angI;
                                double radius = (x * Math.Cos(theta) + y * Math.Sin(theta));
                                int iR = radiusCenter + (int)Math.Round(radius/radialStepSize);

                                if (iR >= 0 && iR < radialSteps)
                                {
                                    accumulator[angI, iR] += weight;

                                    if (accumulator[angI, iR] > maxLine)
                                    {
                                        maxLine = accumulator[angI, iR];
                                    }
                                }
                            }
                        }
                    }
                }
                
                byte[,] houghImage = new byte[angularSteps, radialSteps];

                if (maxLine > 0)
                {
                    for (int angI = 0; angI < angularSteps; angI++)
                    {
                        for (int iR = 0; iR < radialSteps; iR++)
                        {
                            houghImage[angI, iR] = (byte)((accumulator[angI, iR] * 255) / maxLine);
                        }
                    }
                }
                return houghImage;
            }
            
            //peakfinding related
            public static List<(int theta, int r)> PeakFinding(byte[,] houghImage, int t_peak)
            {
                int iw = houghImage.GetLength(0);
                int ih = houghImage.GetLength(1);
                
                List<(int theta, int r)> peaks = new List<(int theta, int r)>();

                for (int theta = 1; theta < iw - 1; theta++)
                {
                    for (int r = 1; r < ih - 1; r++)
                    {
                        byte currentVal = houghImage[theta, r];
                        if (houghImage[theta, r] >= t_peak)
                        {
                            bool maximum = true;

                            for (int dTheta = -1; dTheta <= 1; dTheta++)
                            {
                                for (int dR = -1; dR <= 1; dR++)
                                {
                                    if (dTheta == 0 && dR == 0) 
                                        continue;
                                    if (houghImage[theta + dTheta, r + dR] >= currentVal)
                                    {
                                        maximum = false;
                                        break;
                                    }
                                }
                                if (!maximum) 
                                    break;
                            }
                            if (maximum)
                                peaks.Add((theta, r));
                        }
                        
                    }
                }
                return peaks;
            }
            
            public static byte[,] BytePeaks(List<(int theta, int r)>peaks, int angularSteps, int radialSteps) 
            {
            byte[,] peak = new byte[angularSteps, radialSteps];

            foreach (var (theta, r) in peaks)
            {
                peak[theta, r] = 255;
            }

            return peak;
            }

        
            public static byte [,] SLRFloodfill(byte[,] inputImage)
            {
                byte label = 1;
                int iW = inputImage.GetLength(0);
                int iH = inputImage.GetLength(1);

                for(int u = 0; u < iW; u++)
                for(int v = 0; v < iH; v++)
                    {
                        if (inputImage[u,v] == 255)
                        {   
                            inputImage = Floodfill_1(inputImage, (u,v), iW, iH, label);
                            label++;
                        }
                    }
                return inputImage;
            }
        
            private static byte [,] Floodfill_1(byte [,] input, (int, int) currentLocation, int iW, int iH, byte label)
            {
                //this floodfill crashes the program currently so we need a faster way
                //https://davdata.nl/math/floodfill-old.html (look into this on the weekend)
                var (u, v) = currentLocation;
                input[u,v] = label;

                for(int i = -1; i <= 1; i++)
                for(int j = -1; j <= 1; j++)
                {
                    if(i == 0 && j == 0){continue;}
                    int x = u+i;
                    int y = v+j;
                    if(x < 0 || x >= iW || y < 0 || y >= iH){continue;}

                    if(input[x, y] == 255)
                        {
                            Floodfill_1(input, (x,y), iW, iH,label);
                        }
                }
                return input;
            }

            public byte [,] RegionContours(byte [,] inputImage)
            {
                //according to this exercise we can pretend the image has one shape only, but I will add several inner contours
                //in my test image to show it but idk if they want it so lets make the outercontour thing first
                //they kinda suggest using a list so i will save the contours in lists where the gray value goes up for every next one

                int iW = inputImage.GetLength(0);
                int iH = inputImage.GetLength(1);
                List<((int, int),int)> contourList = new List<((int, int), int)>();

                for(int u = 0; u <iW; u++)
                for(int v = 0; v <iH; v++)
                    {
                        //Gives the first pixel that is part of the contour
                        if(inputImage[u,v] == 255)
                        {
                            contourList = TraceContour(inputImage, (u,v));
                        }
                    }

                //construct the new image from the contourlist
                byte[,] outputImage = new byte[iW, iH];

                int contourLength = contourList.Count;
                int value = 0;
                foreach(((int x, int y), int _) in contourList)
                {
                    outputImage[x,y] = (byte) (1 + (value * 254 / contourLength));
                    value++;
                }

                return outputImage;
            }

            public List<((int,int), int)> TraceContour(byte [,] inputImage, (int x, int y) location)
            {
                //this starts with a FOUND pixel. 
                List<((int, int), int)> contourList = new List<((int, int), int)>();
                ((int, int), int) firstPixel = (location, 0);
                contourList.Add(firstPixel);
                ((int, int), int) secondPixel = NextLocation(inputImage, firstPixel);
                contourList.Add(secondPixel);

                //Sets the second pixel as the current pixel
                ((int, int), int) currentPixel = secondPixel;

                while (currentPixel != firstPixel && NextLocation(inputImage, currentPixel) != secondPixel)
                {
                    
                }



                
                return contourList;
            }

            public ((int, int), int) NextLocation(byte[,] inputImage, ((int, int), int) locationAndDirection)
            {
                return (locationAndDirection);
            }
        }
    }
}         
