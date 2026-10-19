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

namespace ImageApp;

public partial class MainWindow : Window
{
    public class Assignment1
    {
        // ====================================================================
        // ==================== GIVEN (already implemented) ==================
        // ====================================================================

        /// <summary>
        /// Converts loaded color pixel data (<c>[x, y, channel]</c> where channel 0=R, 1=G, 2=B)
        /// to single-channel grayscale by averaging RGB values.
        /// </summary>
        /// <param name="colorPixels">The 3D array of color pixels extracted at load time.</param>
        /// <returns>A 2D array of grayscale byte intensities with values in [0, 255].</returns>
        public byte[,] ConvertToGrayscale(byte[,,] colorPixels)
        {
            int w = colorPixels.GetLength(0);
            int h = colorPixels.GetLength(1);
            byte[,] gray = new byte[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int r = colorPixels[x, y, 0];
                int g = colorPixels[x, y, 1];
                int b = colorPixels[x, y, 2];
                gray[x, y] = (byte)((r + g + b) / 3);
            }

            return gray;
        }

        // ====================================================================
        // ==================== FUNCTIONS TO IMPLEMENT =======================
        // ====================================================================

        /// <summary>
        /// Inverts the intensity values of the input grayscale image.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <returns>A new 2D grayscale image with inverted intensities.</returns>
        public byte[,] InvertImage(byte[,] inputImage)
        {
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);

            byte[,] invertedImage = new byte[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int val = inputImage[x, y];
                invertedImage[x, y] = (byte)(255 - val );
            }

            return invertedImage;
        }

        /// <summary>
        /// Adjusts the contrast of the input grayscale image.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <returns>A new 2D grayscale image with adjusted contrast.</returns>
        public byte[,] AdjustContrast(byte[,] inputImage)
        {
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            int aLow = 255;
            int aHigh = 0;

            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int val = inputImage[x,y];
                if(val < aLow)
                {
                    aLow = val;
                }
                if(val > aHigh)
                {
                    aHigh = val;
                }
            }
            if (aHigh == aLow)
            {
                return inputImage;
            }
            float adjustment = 255f / (aHigh-aLow);

            byte[,] contrastImage = new byte[w,h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int val = inputImage[x, y];
                contrastImage[x, y] = (byte)((val - aLow) * adjustment);
            }

            return contrastImage;
        }

        /// <summary>
        /// Generates a normalized 2D Gaussian filter kernel of the specified size and standard deviation.
        /// </summary>
        /// <param name="size">Kernel dimension (odd integer, e.g. 3, 5, 7).</param>
        /// <param name="sigma">Gaussian standard deviation parameter.</param>
        /// <returns>A 2D float array representing the normalized filter kernel.</returns>
        public float[,] CreateGaussianFilter()
        {
            if (kernelSize < 1 || kernelSize % 2 == 0)
                throw new ArgumentException("Kernel size must be a positive odd integer", nameof(kernelSize));
            
            // create the filter
            float[,] filter = new float[kernelSize, kernelSize];
            
            //calculates the center based on the input size
            int center = kernelSize / 2;
            float sigSquared = 2 * (sigmaValue * sigmaValue);
            
            //setup for the normalization
            float sum = 0;
            
            //loop through given size
            for (int x = 0; x < kernelSize; x++)
            for (int y = 0; y < kernelSize; y++)
            {
                //distance from pixel coordinate to center
                float dx = x - center;
                float dy = y - center;
                
                //radius based on dx and dy
                float r2 = (dx * dx) + (dy * dy);
                
                //calculates the gaussian value on the coordinate, assigns it on the matrix position
                // and adds the value to the sum
                float gaussianValue = (float)Math.Exp(-(r2/sigSquared));
                filter[x, y] =  gaussianValue;
                sum += gaussianValue;
            }
            
            // normalizes the value in the filter matrix by dividing the assigned coordinate with the sum of g-values
            for(int x = 0; x < kernelSize; x++)
            for (int y = 0; y < kernelSize; y++)
                filter[x, y] /= sum;
            
            return filter;
        }

        /// <summary>
        /// Convolves a grayscale image with a given 2D filter kernel.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="filter">The 2D filter kernel to apply.</param>
        /// <returns>The convolved grayscale image.</returns>
        public byte[,] ConvolveImage(byte[,] inputImage, float[,] filter)
        {
            //create dimensions of input image 
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);
            
            //create dimensions of kernel
            int kW = filter.GetLength(0);
            int kH = filter.GetLength(1);
            
            //center of kernel
            int centerX = kW / 2;
            int centerY = kH / 2;
            
            byte[,] resultImage = new byte[iW, iH];
            
            //image loop
            for(int u = 0; u < iW; u++)
            for(int v = 0; v < iH; v++)
            {
                float sum = 0;
                
                //loop over kernel positions based on (0,0) center
                for(int i = -centerX; i <= centerX; i++)
                for(int j = -centerY; j <= centerY; j++)
                {
                    //border of the image(u,v) based on the kernel(i,j)
                    int uBorder = Math.Clamp(u + i, 0, iW - 1);
                    int vBorder = Math.Clamp(v + j, 0, iH - 1);
                    
                    float coefficient = filter[i + centerX, j + centerY];
                    sum += inputImage[uBorder, vBorder] * coefficient;
                }
                
                int pixelVal = (int)Math.Round(sum);
                resultImage[u, v] = (byte)Math.Clamp(pixelVal, 0, 255);
            }

            return resultImage;
        }

        /// <summary>
        /// Applies a median filter of the given kernel size to reduce noise.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="kernelSize">The size of the local neighborhood window (odd integer).</param>
        /// <returns>The filtered grayscale image.</returns>
        public byte[,] MedianFilter(byte[,] inputImage)
        {
            //create dimensions of input image 
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);
            
            byte[,] resultImage = new byte[iW, iH];

            //center of kernel
            int center = kernelSize / 2;

            for(int u = 0; u < iW; u++)
            for(int v= 0; v < iH; v++)
            {
                List<byte> kernelList = new List<byte>();
                for(int i = -center; i <= center; i++)
                for(int j = -center; j <= center; j++)
                {
                    int xOffset = u + i;
                    int yOffset = v + j;
                    //If pixels are out of bound they do not get added to the list
                    if (xOffset >= 0 && xOffset < iW)
                            {
                                if(yOffset >= 0 && yOffset < iH)
                                {
                                    byte kernelByte = inputImage[xOffset,yOffset];
                                    kernelList.Add(kernelByte);
                                }
                            }
                }
                if(kernelList.Count != 0){
                    kernelList.Sort();
                    byte median = kernelList[kernelList.Count/2];
                    resultImage[u,v] = median;
                }
            }
            return resultImage;
        }

        /// <summary>
        /// Computes edge magnitude from horizontal and vertical derivative kernels.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="horizontalKernel">Horizontal gradient kernel.</param>
        /// <param name="verticalKernel">Vertical gradient kernel.</param>
        /// <returns>The edge gradient magnitude image.</returns>
        public byte[,] EdgeMagnitude(
            byte[,] inputImage,
            sbyte[,] horizontalKernel,
            sbyte[,] verticalKernel
        )
        {
            // create temporary grayscale image
            byte[,] tempImage = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            // TODO: add your functionality and checks, think about border handling and type conversion (negative values!)
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);
            int center = 1;
            byte[,] resultImage = new byte[iW, iH];
            _gradientDirection = new double[iW,iH];

            //Loops over the image
            for(int u = 0; u < iW; u++)
            for(int v= 0; v < iH; v++)
            {
                double dx = 0;
                double dy = 0;
                //Loops over kernel positions based on (0,0) center
                for(int i = -center; i <= center; i++)
                for(int j = -center; j <= center; j++)
                {
                    int xo = Math.Clamp(u + i, 0, iW -1);
                    int yo = Math.Clamp(v + j, 0, iH - 1);
                    dx += horizontalKernel[j + center,i + center] * inputImage[xo, yo];
                    dy += verticalKernel[j + center,i + center] * inputImage[xo, yo];
                }
                int magnitude = Convert.ToInt32(Math.Round(Math.Sqrt(dx * dx + dy * dy)));
                resultImage[u, v] = (byte)Math.Clamp(magnitude, 0, 255);
                _gradientDirection[u,v] = Math.Atan2(dy, dx);
            }

            return resultImage;
        }

        /// <summary>
        /// Thresholds a grayscale image into a binary representation based on a cutoff value.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="threshold">Intensity threshold cutoff value in [0, 255].</param>
        /// <returns>A binary image represented as byte intensities (e.g. 0 and 255).</returns>
        public byte[,] ThresholdImage(byte[,] inputImage, int thresh)
        {
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            
            byte[,] threshImage = new byte[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int val = inputImage[x, y];
                if (val <= thresh)
                {
                    threshImage[x, y] = 0;
                }
                else
                {
                    threshImage[x, y] = 255;
                }
                
            }

            return threshImage;
        }

        /// <summary>
        /// Performs morphological binary erosion using the provided structuring element.
        /// </summary>
        /// <param name="inputImage">The binary input image.</param>
        /// <param name="structElem">2D boolean structuring element (true = foreground).</param>
        /// <returns>The eroded binary image.</returns>
        public byte[,] BinaryErodeImage(byte[,] inputImage, bool[,] structElem)
        {
            //set output image size
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);
            
            //set structuring element size
            int kW = structElem.GetLength(0);
            int kH = structElem.GetLength(1);
            
            //set center of structuring element
            int centerX = kW / 2;
            int centerY = kH / 2;
            
            byte[,] output = new byte[iW, iH];
            
            //loops through image
            for (int u = 0; u < iW; u++)
            {
                for (int v = 0; v < iH; v++)
                {
                    bool overlap = true;
                    //loops through structuring element
                    for (int i = -centerX; i <= centerX; i++)
                    {
                        for (int j = -centerY; j <= centerY; j++)
                        {   
                            //checks only if foreground
                            bool foreground = structElem[i + centerX, j + centerY];
                            if (foreground)
                            {
                              int posU = u + i;
                              int posV = v + j;
                              
                              if(posU >= 0 && posU < iW && posV >= 0 && posV < iH)
                                  if (inputImage[posU, posV] == 0)
                                      overlap = false;
                            }
                            
                        }
                    }

                    if (overlap)
                        output[u, v] = 255;
                    else
                        output[u, v] = 0;
                }
            }
            return output;
        }

        /// <summary>
        /// Performs morphological binary dilation using the provided structuring element.
        /// </summary>
        /// <param name="inputImage">The binary input image.</param>
        /// <param name="structElem">2D boolean structuring element (true = foreground).</param>
        /// <returns>The dilated binary image.</returns>
        public byte[,] BinaryDilateImage(byte[,] inputImage, bool[,] structElem)
        {
            //set output image size
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);
            
            //set structuring element size
            int kW = structElem.GetLength(0);
            int kH = structElem.GetLength(1);
            
            //set center of structuring element
            int centerX = kW / 2;
            int centerY = kH / 2;
            
            byte[,] output = new byte[iW, iH];
            
            //loops through image
            for (int u = 0; u < iW; u++)
            {
                for (int v = 0; v < iH; v++)
                {
                    bool hit = false;
                    //loops through structuring element
                    for (int i = -centerX; i <= centerX; i++)
                    {
                        for (int j = -centerY; j <= centerY; j++)
                        {   
                            //checks only if foreground
                            bool foreground = structElem[i + centerX, j + centerY];
                            if (foreground)
                            {
                                int posU = u + i;
                                int posV = v + j;
                              
                                if(posU >= 0 && posU < iW && posV >= 0 && posV < iH)
                                    if (inputImage[posU, posV] == 255)
                                        hit = true;
                            }
                            
                        }
                    }

                    if (hit)
                        output[u, v] = 255;
                    else
                        output[u, v] = 0;
                }
            }
            return output;
        }

        /// <summary>
        /// Performs morphological binary opening (erosion followed by dilation).
        /// </summary>
        /// <param name="inputImage">The binary input image.</param>
        /// <param name="structElem">2D boolean structuring element (true = foreground).</param>
        /// <returns>The opened binary image.</returns>
        public byte[,] BinaryOpenImage(byte[,] inputImage, bool[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            byte[,] erodedImage = BinaryErodeImage(inputImage, structElem);
            
            byte[,] openedImage = BinaryDilateImage(erodedImage, structElem);
            
            output = openedImage;
            return output;
        }

        /// <summary>
        /// Performs morphological binary closing (dilation followed by erosion).
        /// </summary>
        /// <param name="inputImage">The binary input image.</param>
        /// <param name="structElem">2D boolean structuring element (true = foreground).</param>
        /// <returns>The closed binary image.</returns>
        public byte[,] BinaryCloseImage(byte[,] inputImage, bool[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
                        
            byte[,] erodedImage = BinaryDilateImage(inputImage, structElem);
            
            byte[,] closedImage = BinaryErodeImage(erodedImage, structElem);
            
            output = closedImage;
            _foregroundPixels = ForegroundPixels(output);
            
            return output;
        }

        /// <summary>
        /// Performs morphological grayscale erosion using the provided structuring element.
        /// </summary>
        /// <param name="inputImage">The 2D grayscale input image.</param>
        /// <param name="structElem">2D integer structuring element defining neighborhood offsets.</param>
        /// <returns>The eroded grayscale image.</returns>
        public byte[,] GrayscaleErodeImage(byte[,] inputImage, int[,] structElem)
        {
            //set output image size
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);
            
            //set structuring element size
            int kW = structElem.GetLength(0);
            int kH = structElem.GetLength(1);
            
            //set center of structuring element
            int centerX = kW / 2;
            int centerY = kH / 2;
            
            byte[,] output = new byte[iW, iH];
            byte min = 0;
            //loop through image
            for (int u = 0; u < iW; u++)
            {
                for (int v = 0; v < iH; v++)
                {
                    byte minVal = 255;
                    
                    //loop through structure element
                    for (int i = -centerX; i <= centerX; i++)
                    {
                        for (int j = -centerY; j <= centerY; j++)
                        {
                            if (structElem[i + centerX, j + centerY] > 0)
                            {
                                int posU = Math.Clamp(u + i, 0, iW - 1);
                                int posV = Math.Clamp(v + j, 0, iH - 1);

                                byte pixelVal = inputImage[posU, posV];
                                
                                if (pixelVal < minVal)
                                    minVal = pixelVal;
                            }
                        }
                    }
                    output[u, v] = minVal;
                }
            }
            return output;
        }

        /// <summary>
        /// Performs morphological grayscale dilation using the provided structuring element.
        /// </summary>
        /// <param name="inputImage">The 2D grayscale input image.</param>
        /// <param name="structElem">2D integer structuring element defining neighborhood offsets.</param>
        /// <returns>The dilated grayscale image.</returns>
        public byte[,] GrayscaleDilateImage(byte[,] inputImage, int[,] structElem)
        {
            //set output image size
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);
            
            //set structuring element size
            int kW = structElem.GetLength(0);
            int kH = structElem.GetLength(1);
            
            //set center of structuring element
            int centerX = kW / 2;
            int centerY = kH / 2;
            
            byte[,] output = new byte[iW, iH];
            byte min = 0;
            //loop through image
            for (int u = 0; u < iW; u++)
            {
                for (int v = 0; v < iH; v++)
                {
                    byte maxVal = 0;
                    
                    //loop through structure element
                    for (int i = -centerX; i <= centerX; i++)
                    {
                        for (int j = -centerY; j <= centerY; j++)
                        {
                            if (structElem[i + centerX, j + centerY] > 0)
                            {
                                int posU = Math.Clamp(u + i, 0, iW - 1);
                                int posV = Math.Clamp(v + j, 0, iH - 1);

                                byte pixelVal = inputImage[posU, posV];
                                
                                if (pixelVal > maxVal)
                                    maxVal = pixelVal;
                            }
                        }
                    }
                    output[u, v] = maxVal;
                }
            }
            return output;
        }

        public byte[,] HistogramMatching(byte[,] inputImage, byte[,] referenceImage)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];

            //Creates a normalised histogram of both the input image and the reference image
            SortedDictionary<int, float> normInput = NormalisedHistogram(inputImage);
            SortedDictionary<int, float> normRef = NormalisedHistogram(referenceImage);            

            //Creates a lookup table
            SortedDictionary<int, int> lookUp = CreateLookUpTable(normInput, normRef);

            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            for(int u = 0; u < w; u++)
            for(int v = 0; v < h; v++)
                {
                    byte val = inputImage[u,v];
                    output[u,v] = (byte)lookUp[val];       
                }
            return output;
        }



        private static SortedDictionary<int, float> NormalisedHistogram(byte[,] inputImage)
        {
            //Creates a histogram
            SortedDictionary<int,int> histogram = new SortedDictionary<int, int>();
            //Empy histogram
            for(int i = 0; i < 256; i++)
            {
                histogram[i] = 0;
            }
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            for(int u = 0; u < w; u++)
            for(int v = 0; v < h; v++)
            {
                histogram[inputImage[u,v]]++;
            }

            //Creates a cumulative histogram
            SortedDictionary<int, int> cumHistogram = new SortedDictionary<int, int>();
            cumHistogram[0] = histogram[0];
            for(int n = 1; n < 256; n++)
            {
                cumHistogram[n] = cumHistogram[n-1] + histogram[n];
            }

            //Creates a normalised histogram
            int size = w * h;
            SortedDictionary<int,float> normHistogram = new SortedDictionary<int, float>();
            for(int n = 0; n < 256; n++)
            {
                normHistogram[n] = (float)cumHistogram[n]/size;
            }
            return normHistogram;
        }

        private static SortedDictionary<int, int> CreateLookUpTable(SortedDictionary<int, float> original, SortedDictionary<int, float> reference)
        {
            SortedDictionary<int, int> lookUp = new SortedDictionary<int, int>();
            for(int n = 0; n < 256; n++)
            {
                for(int m = 0; m < 256; m++)
                {
                    if(original[n] <= reference[m])
                    {
                        lookUp[n] = m;
                        break;
                    }
                }
            }
            return lookUp;
        }

        public bool [,] CreateStructureElement()
        {
            bool [,] strucElement = new bool [strucElemSize, strucElemSize];
            for(int i = 0; i < strucElemSize; i++)
            for(int j = 0; j < strucElemSize; j++)
                {
                    strucElement[i,j] = true;
                }
            return strucElement;
        }
        public int [,] CreateGrayStructureElement()
        {
            int [,] strucElement = new int [strucElemSize, strucElemSize];
            for(int i = 0; i < strucElemSize; i++)
            for(int j = 0; j < strucElemSize; j++)
            {
                strucElement[i,j] = 100;
            }
            return strucElement;
        }

        
        public byte[,] CannyEdgeDetection(byte[,] inputImage)
        {  
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);
            byte [,] output = new byte[iW, iH];

            //This is the preprocessing step
            byte[,] gaussian = ConvolveImage(inputImage, CreateGaussianFilter());
            byte[,] edgeMagnitude = EdgeMagnitude(gaussian, horizontalKernel, verticalKernel);

            //Non Maximum Supression
            output = NonMaximumSuppression(edgeMagnitude);

            //Double tresholding
            int [,] thresholded = DoubleThreshold(output);
            
            //Hysteresis Thresholding
            output = HystereresisThresholding(thresholded);

            return output;
        }

        private byte [,] NonMaximumSuppression(byte [,] edgeMagnitude)
        {
            int iW = edgeMagnitude.GetLength(0);
            int iH = edgeMagnitude.GetLength(1);
            byte [,] output = new byte[iW, iH];

            for(int u = 0; u < iW; u++)
            for(int v = 0; v < iH; v++)
                {
                    double angleDeg = _gradientDirection[u,v] * 180.0 / Math.PI;
                    if (angleDeg < 0){angleDeg+= 180;}

                    //Depending on the angle we want to check different neighbours
                    if (angleDeg >= 0 && angleDeg < 22.5 || angleDeg >= 157.5)
                    {
                        //Left and right neighbour
                        byte l = edgeMagnitude[Math.Clamp(u - 1, 0, iW- 1),v];
                        byte m = edgeMagnitude[u,v];
                        byte r = edgeMagnitude[Math.Clamp(u + 1, 0, iW- 1), v];
                        if(m >= l && m >= r){output[u,v] = m;}
                        else{output[u,v] =0;}
                    }
                    else if(angleDeg >= 22.5 && angleDeg < 67.5)
                    {
                        //Bottom-Right and Top-Left Neighbour
                        byte tr = edgeMagnitude[Math.Clamp(u + 1, 0, iW- 1), Math.Clamp(v + 1, 0, iH- 1)];
                        byte m = edgeMagnitude[u,v];
                        byte bl = edgeMagnitude[Math.Clamp(u - 1, 0, iW- 1), Math.Clamp(v - 1, 0, iH- 1)];
                        if(m >= tr && m >= bl){output[u,v] = m;}
                        else{output[u,v] = 0;}
                    }
                    else if(angleDeg >= 67.5 && angleDeg < 112.5)
                    {
                        //Top and Bottom Neighbour
                        byte t = edgeMagnitude[u, Math.Clamp(v - 1, 0, iH- 1)];
                        byte m = edgeMagnitude[u,v];
                        byte b = edgeMagnitude[u, Math.Clamp(v + 1, 0, iH- 1)];
                        if(m >= t && m >= b){output[u,v] = m;}
                        else{output[u,v] = 0;}
                    }
                    else if(angleDeg >= 112.5 && angleDeg < 157.5)
                    {
                        //Bottom-Left and Top-Right neighbour
                        byte tl = edgeMagnitude[Math.Clamp(u - 1, 0, iW- 1), Math.Clamp(v + 1, 0, iH- 1)];
                        byte m = edgeMagnitude[u,v];
                        byte br = edgeMagnitude[Math.Clamp(u + 1, 0, iW- 1), Math.Clamp(v - 1, 0, iH- 1)];
                        if(m >= tl && m >= br){output[u,v] = m;}
                        else{output[u,v] = 0;}
                    }
                }

            return output;
        }
        private int [,] DoubleThreshold(byte [,] localised)
        {
            int iW = localised.GetLength(0);
            int iH = localised.GetLength(1);
            int [,] thresholded = new int [iW, iH];

            for(int u = 0; u < iW; u++)
            for(int v = 0; v < iH; v++)
                {
                    //0 means no edge, 1 means weak edge, 2 means strong edge
                    if(localised[u,v] >= highThreshold)
                    {
                        thresholded[u,v] = 2;
                    }
                    else if(localised[u,v] >= lowThreshold)
                    {
                        thresholded[u,v] = 1;
                    }
                    else{thresholded[u,v] = 0;}
                }
            return thresholded;
        }

        private byte[,] HystereresisThresholding(int [,] input)
        {
            int iW = input.GetLength(0);
            int iH = input.GetLength(1);   
            byte [,] output = new byte [iW, iH];

            List <(int, int)> locations = [];

            for(int u = 0; u < iW; u++)
            for(int v = 0; v < iH; v++)
                {
                    //2 = Strong edge
                    if(input[u,v] == 2)
                    {
                        output[u,v] = 255;
                        for(int i = -1; i <= 1; i++)
                        for(int j = -1; j <= 1; j++)
                            {
                                if (input[Math.Clamp(u+i, 0, iW- 1), Math.Clamp(v+j, 0, iH- 1)] == 1)
                                {
                                    int x = Math.Clamp(u+i, 0, iW- 1);
                                    int y = Math.Clamp(v+j, 0, iH- 1);
                                    List <(int, int)> location = new List<(int, int)> {(x, y)};
                                    locations.AddRange(Floodfill(input, location, (x, y), iW, iH));
                                }
                            }
                    }
                    //0 = No edge
                    else{output[u,v] = 0;}
                }
            foreach ((int x, int y) in locations)
            {
                output[x,y] = 255;
            }
            return output;
        }

        private List<(int,int)> Floodfill(int [,] input, List<(int, int)> visitedLocations, (int, int) currentLocation, int iW, int iH)
        {
            var (u, v) = currentLocation;
            for(int i = -1; i <= 1; i++)
            for(int j = -1; j <= 1; j++)
            {
                if(i == 0 && j == 0){continue;}
                
                int x = Math.Clamp(u+i, 0, iW - 1);
                int y = Math.Clamp(v+j, 0, iH -1);
                if(input[x, y] == 1)
                if(!visitedLocations.Contains((x, y)))
                    {
                        visitedLocations.Add((x,y));
                        Floodfill(input, visitedLocations, (x,y), iW, iH);
                    }
            }
            return visitedLocations;
        }
        
        public byte[,] CreateTask1(byte[,] inputImage, int filterType, int kernelSize, sbyte[,] horizontalkernel, sbyte[,] verticalkernel, int threshold)
        { 
            byte[,] smoothedImage; 

            if (filterType == 0) 
            { 
                float[,] gaussianKernel = CreateGaussianFilter(); 
                smoothedImage = ConvolveImage(inputImage, gaussianKernel); 
            }
            else 
            { 
                smoothedImage = MedianFilter(inputImage); 
            }
            byte[,] edgeImage = EdgeMagnitude(smoothedImage, horizontalkernel, verticalkernel); 
            byte[,] binaryOutput = ThresholdImage(edgeImage, threshold); 
            return binaryOutput;
            
        }

        public byte [,] CreateTask2(byte[,] inputImage)
        {
            byte [,] output = AdjustContrast(inputImage);
            int [,] strucElem = CreateGrayStructureElement();
            output = GrayscaleErodeImage(output, strucElem);
            return output;
        }
        
        public byte [,] CreateTask3(byte [,] inputImage)
        {
            bool [,] strucElem = CreateStructureElement();
            byte [,] output = BinaryCloseImage(inputImage, strucElem);
            return output;

        }
        
        private int ForegroundPixels(byte [,] image)
        {
            int foregroundPixels = 0;
            int iW = image.GetLength(0);
            int iH = image.GetLength(1);

            for (int u = 0; u < iW; u++)
            for (int v = 0; v < iH; v++)
                {
                    if(image[u,v] == 255)
                    {
                        foregroundPixels += 1;
                    }
                }
            return foregroundPixels;
        }

        public int CountDistinctValues(byte[,] image)
        {
            int width = image.GetLength(0); 
            int height = image.GetLength(1); 
            bool[] seen = new bool[256]; 
            int count = 0;
            for (int u = 0; u < width; u++)
            {
                for (int v = 0; v < height; v++)
                {
                    byte val = image[u, v];
                    if (!seen[val])
                    {
                        seen[val] = true; 
                        count++;
                    }
                }
            } return count;
        }

        public double CalculateAverageIntensity(byte[,] image)
        {
            int width = image.GetLength(0); 
            int height = image.GetLength(1); 
            double sum = 0;
            for (int u = 0; u < width; u++)
            {
                for (int v = 0; v < height; v++)
                {
                    sum += image[u, v];
                }
            } return sum / (width * height);
        }

        private int[] AnalyzeDistinctValues(byte[,] inputImage)
        {
            int[] sizes = new int[] { 3, 7, 11, 15, 19 }; 
            int[] D = new int[sizes.Length]; 
            
            for (int k = 0; k < sizes.Length; k++) 
            { 
                int[,] structElem = CreateGrayStructureElement(); 
                byte[,] Ci = GrayscaleErodeImage(inputImage, structElem); 
                D[k] = CountDistinctValues(Ci); 
            } 
            return D;
        }

        private double[] AnalyzeAverageIntensity(byte[,] inputImage)
        {
            int[] sizes = new int[] { 3, 7, 11, 15, 19 }; 
            double[] E = new double[sizes.Length];
            for (int k = 0; k < sizes.Length; k++)
            {
                int[,] structElem = CreateGrayStructureElement(); 
                byte[,] Ci = GrayscaleErodeImage(inputImage, structElem); 
                E[k] = CalculateAverageIntensity(Ci);
            } 
            return E;
        }
    }
}

