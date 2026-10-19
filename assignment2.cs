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

        public class Assignment2
        {
            private Assignment1 a1 = new Assignment1();
            
            //here comes the sunshine

            public byte[,] SequentialRegionLabeling(byte[,] inputImage)
            {
                //to do
                //something is wrong with the labelling the colours are darker than if i use the floodfill function
                //which means something is wrong
                int iW = inputImage.GetLength(0);
                int iH = inputImage.GetLength(1);

                Dictionary<byte, byte> parent = new Dictionary<byte, byte>();

                (int i, int j)[] neighbours =
                {
                    (-1, -1),
                    (-1, 0),
                    (-1, 1),
                    (0, -1)
                };

                byte[,] labeledImage = new byte [iW, iH];

                //The label value that keeps track of which label value we are on
                byte label = 2;

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
                            int uOffset = u + i;
                            int vOffset = v + j;
                            if (uOffset < 0 || uOffset >= iW || vOffset < 0 || vOffset >= iH){continue;}

                            byte labelVal = labeledImage[uOffset, vOffset];

                            //Sees if there is a lower label value in one of the neighbouring pixels
                            //we need to add all 
                            if(labelVal > 0 && tempLabel < 255 && labelVal != tempLabel)
                            {
                                parent = Union(parent, tempLabel, labelVal);
                            }

                            if (labelVal > 0 && labelVal < tempLabel)
                            {
                                tempLabel = labelVal;
                            }
                           
                        }

                        //If no neighbouring label was found create a new label
                        if (tempLabel == 255)
                        {
                            labeledImage[u, v] = label;
                            if (label != 254){
                                label++;}
                            //not above 254 values but eh (for now)
                        }
                        else
                        {
                            labeledImage[u, v] = tempLabel;
                        }
                    }
                }

                labeledImage = ResolveCollisions(labeledImage, parent, iW, iH);

                return labeledImage;
            }

            private byte[,] ResolveCollisions(byte[,] labeledImage, Dictionary<byte, byte> parent, int iW, int iH)
            {
                //Second pass this needs to go a bit differently
                //because we need to find all the labels that are connected

                Dictionary<byte, byte> updatedDict = new Dictionary<byte, byte>();
                //we need to relabel all the dictionary to new good labels apparently
                //this is the only thing basically lol

                //update the labeled image
                for (int u = 0; u < iW; u++)
                for (int v = 0; v < iH; v++)
                {
                    if (parent.ContainsKey(labeledImage[u, v]))
                    {
                        labeledImage[u, v] = Find(labeledImage[u, v], parent);
                    }
                }

                return labeledImage;
            }

            private byte Find(byte x, Dictionary<byte, byte> parent)
            {
                if (parent[x] != x)
                {
                    parent[x] = Find(parent[x], parent);
                }

                return parent[x];
            }

            private Dictionary<byte, byte> Union(Dictionary<byte, byte> parent, byte a, byte b)
            {
                //https://stackoverflow.com/questions/46383172/disjoint-set-implementation-in-c-sharp
                //https://www.geeksforgeeks.org/dsa/introduction-to-disjoint-set-data-structure-or-union-find-algorithm/

                if (!parent.ContainsKey(a))
                {
                    parent[a] = a;
                }

                if (!parent.ContainsKey(b))
                {
                    parent[b] = b;
                }

                byte rootA = Find(a, parent);
                byte rootB = Find(b, parent);

                if (rootA == rootB)
                {
                    return parent;
                }

                if (rootA < rootB)
                {
                    parent[rootB] = rootA;
                }
                else
                {
                    parent[rootA] = rootB;
                }

                return parent;
            }
            

              public byte[,] HoughTransform(byte[,] inputImage, int isBinary = 0, sbyte[,] horizontalKernel = null,  sbyte[,] verticalKernel = null)
            {
                //For customization of binary and grayscale.
                //If you want binary, it uses canny edge detection and for grayscale edge the edge magnitude
                byte[,] filteredImage = BinaryOrGrayscale(inputImage, isBinary, horizontalKernel, verticalKernel);
                /*if (isBinary == 0)
                    filteredImage = EdgeMagnitude(inputImage, horizontalKernel, verticalKernel);*/
                
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
                        
                        if (filteredImage[u, v] > 0)
                        {
                            int weight;
                            if (isBinary == 1)
                                weight = 1;
                            else
                                weight = filteredImage[u, v];    
                            
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
            
            //Hough peakfinding related
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
            
            //Hough line segments
            public (List<((int x1, int y1), (int x2, int y2))> segments, byte[,,] overlayImage) 
            HoughLineSegments(byte[,] inputImage, byte[,] houghImage, List<(int theta, int r)> peaks, int minThreshold,
                int minSegLength, int maxGapParam, int isBinary = 0, sbyte[,] horizontalKernel = null,  sbyte[,] verticalKernel = null)
            {
                int iW = inputImage.GetLength(0);
                int iH = inputImage.GetLength(1);
                
                //setup for which edge image and to set overlayimage
                byte[,] filteredImage = BinaryOrGrayscale(inputImage, isBinary,  horizontalKernel, verticalKernel);
                byte[,,] overlayImage = new byte[iW, iH, 3];
                for (int u = 0; u < iW; u++) 
                { 
                    for (int v = 0; v < iH; v++) 
                    { 
                        byte val = filteredImage[u, v]; 
                        overlayImage[u, v, 0] = val; 
                        overlayImage[u, v, 1] = val;
                        overlayImage[u, v, 2] = val;
                    }
                }
                
                int angularSteps = houghImage.GetLength(0); 
                int radialSteps = houghImage.GetLength(1);
                
                //hough related
                int uCenter = iW / 2; 
                int vCenter = iH / 2; 
                int rMax = (int)Math.Sqrt(uCenter * uCenter + vCenter * vCenter);
                double angularStepSize = Math.PI / angularSteps; 
                double radialStepSize = (2.0 * rMax) / radialSteps; 
                int radiusCenter = radialSteps / 2;
                
                var segments = new List<((int x1, int y1), (int x2, int y2))>();
                
                //Go through list
                foreach (var peak in peaks)
                {
                    double theta = peak.theta * angularStepSize;
                    double radius = (peak.r - radiusCenter) * radialStepSize;
                    
                    double cosT = Math.Cos(theta); 
                    double sinT = Math.Sin(theta);
                    
                    bool inSegment = false; 
                    int startX = -1, startY = -1;
                    int lastEdgeX = -1, lastEdgeY = -1; 
                    int gapCount = 0;
                    
                    var currentSegmentPixels = new List<(int u, int v)>(); 
                    var gapBuffer = new List<(int u, int v)>();

                    for (int t = -rMax; t <= rMax; t++)
                    {
                        int u = (int)Math.Round(uCenter + radius * cosT - t * sinT);
                        int v = (int)Math.Round(vCenter + radius * sinT + t * cosT);

                        if (u < 0 || u >= iW || v < 0 || v >= iH)
                            continue;

                        byte pixelVal = filteredImage[u, v];

                        bool isOn = false;

                        if (isBinary == 1)
                            isOn = pixelVal > 0;
                        else
                            isOn = pixelVal >= minThreshold;

                        if (isOn)
                        {
                            if (!inSegment)
                            {
                                inSegment = true;
                                startX = u;
                                startY = v;
                                gapCount = 0;
                                currentSegmentPixels.Clear();
                                gapBuffer.Clear();
                            }
                            else if (gapCount > 0)
                            {
                                currentSegmentPixels.AddRange(gapBuffer);
                                gapBuffer.Clear();
                                gapCount = 0;
                            }

                            lastEdgeX = u;
                            lastEdgeY = v;
                            currentSegmentPixels.Add((u, v));
                        }
                        if (inSegment)
                        {
                            gapCount++;
                            gapBuffer.Add((u, v));
                            if (gapCount > maxGapParam)
                            {
                                FinalizeSegment(startX, startY, lastEdgeX, lastEdgeY,
                                    minSegLength, currentSegmentPixels, segments, overlayImage);
                                inSegment = false;
                                gapCount = 0;
                                currentSegmentPixels.Clear();
                                gapBuffer.Clear();
                            }
                        }
                    }
                    if (inSegment)
                    {
                        FinalizeSegment(startX, startY, lastEdgeX, lastEdgeY, 
                            minSegLength, currentSegmentPixels, segments, overlayImage);
                    }
                }
                
                return (segments, overlayImage);
            }

            private void FinalizeSegment(int x1, int y1, int x2, int y2, int minSegLength,
                List<(int u, int v)> pixelsToDraw,
                List<((int x1, int y1), (int x2, int y2))> segments, byte[,,] overlayImage)
            {
                double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

                if (length >= minSegLength)
                {
                    segments.Add(((x1, y1), (x2, y2)));
                    foreach (var (u, v) in pixelsToDraw)
                    {
                        overlayImage[u, v, 0] = 255;
                        overlayImage[u, v, 1] = 0;
                        overlayImage[u, v, 2] = 0;
                    }
                }
            }

            //For functions to call if it needs either binary edge map or grayscale edge magnitude
            public byte[,] BinaryOrGrayscale(byte[,] inputImage, int isBinary, sbyte[,] horizontalKernel = null,  sbyte[,] verticalKernel = null)
            {
                byte[,] filteredImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
                
                if (isBinary == 0)
                    filteredImage = a1.EdgeMagnitude(inputImage, horizontalKernel, verticalKernel);
                else
                    filteredImage = a1.CannyEdgeDetection(inputImage);
                
                return filteredImage;
            }
            
            public byte[,] BytePeaks(List<(int theta, int r)>peaks, int angularSteps, int radialSteps) 
            {
            byte[,] peak = new byte[angularSteps, radialSteps];

            foreach (var (theta, r) in peaks)
            {
                peak[theta, r] = 255;
            }

            return peak;
            }
        
            //this is to speed up the floodfill algorithm
            private static bool [,] _visited = new bool[0,0];

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
                            _visited = new bool [iW, iH];
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

                //we can use queues instead of figuring out a complicated algorithm :DDD
                Queue<(int,int)> queue = new(); 

                input[currentLocation.Item1,currentLocation.Item2] = label;
                queue.Enqueue(currentLocation);

                while(queue.Count() > 0)
                {
                    var (u,v) = queue.Dequeue();

                    for(int i = -1; i <= 1; i++)
                    for(int j = -1; j <= 1; j++)
                    {
                        if(i == 0 && j == 0){continue;}
                        int x = u+i;
                        int y = v+j;
                        if(x < 0 || x >= iW || y < 0 || y >= iH){continue;}

                        if(_visited[x,y] == false)
                        {
                            if(input[x, y] == 255)
                            {
                                input[x,y] = label;
                                _visited[x,y] = true;
                                queue.Enqueue((x,y));
                            }
                        }
                    }
                }
                return input;
            }

            public byte [,] RegionContours(byte [,] inputImage)
            {
                //according to this exercise we can pretend the image has one shape only, but I will add several inner contours
                //in my test image to show it but idk if they want it so lets make the outercontour thing first
                //they kinda suggest using a list so i will save the contours in lists where the gray value goes up for every next one
                //do we have to do internal contours too?????????????/

                ///IS CURRENTLY INFINITELY BUSY
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
                            break;
                        }
                    }

                //construct the new image from the contourlist
                byte[,] outputImage = new byte[iW, iH];

                int contourLength = contourList.Count;
                int value = 0;
                foreach(((int x, int y), int _) in contourList)
                {
                    outputImage[x,y] = (byte) (50 + (value * 205 / contourLength));
                    value++;
                }

                return outputImage;
            }

            private List<((int,int), int)> TraceContour(byte [,] inputImage, (int x, int y) location)
            {
                //this starts with a FOUND pixel. and saves the values of the next two pixels
                List<((int, int), int)> contourList = new List<((int, int), int)>();
                ((int, int), int) firstPixel = (location, 0);
                contourList.Add(firstPixel);
                var secondPixel = NextLocation(inputImage, firstPixel);
                contourList.Add(secondPixel);

                //If a contour is only one pixel return
                if(secondPixel.Item1 == firstPixel.Item1){return contourList;}

                //Sets the second pixel as the current pixel
                ((int, int), int) currentPixel = secondPixel;

                //creates a hashset for visited pixels
                HashSet<((int,int), int)> visited = new HashSet<((int, int), int)>();
                visited.Add(firstPixel);

                //i assume the infinite loop is here orrr
                //for(int i = 0; i < 255; i++) //ok well the infinite loop is here but this doesnt mean this is fixed now
                while (!visited.Contains(currentPixel)) 
                {
                    visited.Add(currentPixel);
                    contourList.Add(currentPixel);
                    currentPixel = NextLocation(inputImage, currentPixel);
                }

                return contourList;
            }

            
            private static readonly (int dx, int dy)[] Dirs =
            {
                (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)
            };

            private ((int, int), int) NextLocation(byte[,] inputImage, ((int, int), int) locationAndDirection)
            {
                var ((x, y), d) = locationAndDirection;
                var nextLocation = locationAndDirection;
                
                for(int steps = 0; steps < 8; steps++)
                {
                    int i = (steps + d) % 8;
                    var (x1, y1) = Dirs[i];
                    if(x+x1 < 0 || x+x1 >= inputImage.GetLength(0) || y+y1 < 0 || y+y1 >= inputImage.GetLength(1)){continue;}
                    //i assume the infinite loop is here orrr
                    if(inputImage[x + x1, y + y1] == 255)
                    {
                        nextLocation = ((x+ x1, y + y1), (i + 6) % 8);
                        return nextLocation;
                    }
                }

                return nextLocation;
            }
        }
    }
}         
