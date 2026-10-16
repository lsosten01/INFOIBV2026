using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Xml;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;


namespace ImageApp
{
    public partial class MainWindow : Window
    {
        private WriteableBitmap? _loadedBitmap; // the raw loaded image, kept in color, for display in OriginalImage
        private byte[,,]? _loadedColorPixels; // [x, y, channel] with channel 0=R, 1=G, 2=B -- extracted once at load time
        private byte[,,]? _referenceColorPixels;
        private byte[,]? _processedGray; // Processed grayscale values (nullable)
        private double[,] _gradientDirection;
        private int _foregroundPixels = 0;

        Assignment2 a2 = new Assignment2();

        sbyte[,] horizontalKernel =
                                {
                                    {-1, 0, 1},
                                    {-2, 0, 2},
                                    {-1, 0, 1}
                                }; 
        sbyte[,] verticalKernel =
                                {
                                    {-1, -2, -1},
                                    {0, 0, 0},
                                    {1, 2, 1}
                                };

        // Simple fixed defaults used by the functions below until you add your own
        // GUI controls (TextBoxes, ComboBoxes, etc.) to let the user set these values.
        private byte _threshold = 128;

        // Enum for operations. As you implement each function, add a case for it
        // in OnApply below; the dropdown is populated automatically from this list.
        //
        // NOTE: Task1, Task2, and Task3 (from the assignment text) are NOT listed here.
        // You need to add those dropdown entries yourself as part of implementing them.
        private enum ProcessingFunctions
        {
            ConvertToGrayscale,
            InvertImage,
            AdjustContrast,
            ConvolveImage,
            MedianFilter,
            EdgeMagnitude,
            ThresholdImage,
            BinaryErodeImage,
            BinaryDilateImage,
            BinaryOpenImage,
            BinaryCloseImage,
            GrayscaleErodeImage,
            GrayscaleDilateImage,
            HistogramMatching,
            CannyEdgeDetection,
            Task1,
            Task2,
            Task3,
            SequentialRegionLabeling,
            HoughTransform,
            HoughPeakFinding,
            HoughLineSegments,
            Floodfill
        }

        private enum Values
        {
            FilterType,
            KernelSize,
            SigmaValue,
            Threshold,
            HighThreshold,
            LowThreshold,
            StructureElement
        }

        private enum FilterType
        {
            Gaussian,
            Median,
        }

        private int kernelSize = 9;
        private float sigmaValue = 3.0f;
        private int threshold = 110;
        private int filterType = 0;
        private int strucElemSize = 3;
        private int highThreshold = 110;
        private int lowThreshold = 50;

        public MainWindow()
        {
            InitializeComponent();
            var valueBox = this.Get<ComboBox>("ValueBox");

            OperationBox.ItemsSource = Enum.GetValues<ProcessingFunctions>();
            OperationBox.SelectedIndex = 0; // Select first item by default

            valueBox.ItemsSource = Enum.GetValues<Values>();
            valueBox.SelectedIndex = 0;
            
        }

        

        /// <summary>
        /// Opens a file picker dialog, loads the selected image, and extracts its color pixel data.
        /// </summary>
        
        private async Task<byte[,,]?> LoadImagePixels()
        {
            if (!StorageProvider.CanOpen)
            {
                StatusText.Text = "Opening files is not supported on this system.";
                return null;
            }

            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(
                    new()
                    {
                        Title = "Open Image",
                        AllowMultiple = false,
                        FileTypeFilter = [FilePickerFileTypes.ImageAll]
                    }
                );

                var file = files.FirstOrDefault();
                if (file == null)
                    return null;

                await using var stream = await file.OpenReadAsync();

                // Decode the file using Avalonia's own image loader
                using var decoded = new Bitmap(stream);
                var size = decoded.PixelSize;
                int width = size.Width;
                int height = size.Height;

                // Force a known, fixed pixel layout (RGBA, 8 bits per channel, unpremultiplied alpha)
                // so we can reliably read raw bytes regardless of the source file's own format.
                _loadedBitmap?.Dispose();
                _loadedBitmap = new(size, new(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);

                byte [,,]loadedColorPixels = new byte[width, height, 3];
                using (var fb = _loadedBitmap.Lock())
                {
                    decoded.CopyPixels(fb,AlphaFormat.Unpremul);
                    // ^ transcodes the decoded image into our WriteableBitmap's RGBA8888 layout

                    int totalBytes = fb.RowBytes * height;
                    byte[] buffer = new byte[totalBytes];
                    Marshal.Copy(fb.Address, buffer, 0, totalBytes);

                    for (int y = 0; y < height; y++)
                    {
                        int rowStart = y * fb.RowBytes;
                        for (int x = 0; x < width; x++)
                        {
                            int idx = rowStart + x * 4; // 4 bytes per pixel: R, G, B, A
                            loadedColorPixels[x, y, 0] = buffer[idx + 0];
                            loadedColorPixels[x, y, 1] = buffer[idx + 1];
                            loadedColorPixels[x, y, 2] = buffer[idx + 2];
                        }
                    }
                }

                _processedGray = null;
                (OriginalImage.Source as IDisposable)?.Dispose();
                OriginalImage.Source = _loadedBitmap;
                (ProcessedImage.Source as IDisposable)?.Dispose();
                ProcessedImage.Source = null;
                StatusText.Text = $"Loaded image ({width} \u00d7 {height} px).";
                return loadedColorPixels;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load image: {ex.Message}";
                return null;
            }
        }

        private async void OnLoadImage(object? sender, RoutedEventArgs e)
        {
            _loadedColorPixels = await LoadImagePixels();
        }
        private async void OnLoadReference(object? sender, RoutedEventArgs e)
        {
            
            _referenceColorPixels = await LoadImagePixels();
        }
        /// <summary>
        /// Saves the current processed grayscale image to disk as a PNG file.
        /// </summary>
        private async void OnSaveImage(object? sender, RoutedEventArgs e)
        {
            if (_processedGray == null)
            {
                StatusText.Text = "No processed image to save. Apply an operation first.";
                return;
            }

            if (!StorageProvider.CanSave)
            {
                StatusText.Text = "Saving files is not supported on this system.";
                return;
            }

            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(
                    new()
                    {
                        Title = "Save Processed Image",
                        SuggestedFileName = "processed.png",
                        DefaultExtension = "png",
                        FileTypeChoices = [FilePickerFileTypes.ImagePng]
                    }
                );

                if (file == null)
                    return;

                using var bmp = ByteArrayToBitmap(_processedGray);
                await using var stream = await file.OpenWriteAsync();
                bmp.Save(stream);
                StatusText.Text = "Processed image saved successfully.";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to save image: {ex.Message}";
            }
        }

        private async void OnValueChanged(object? sender, SelectionChangedEventArgs e)
        {
            var parameterPanel = this.Get<StackPanel>("Box");
            var valueBox = this.Get<ComboBox>("ValueBox");
            var selected = valueBox. SelectedItem;

            switch (selected)
            {
                //put the numbers where they should be
                case Values.FilterType:
                    parameterPanel.Children.Clear();
                    
                    //Creates a combobox
                    var filterTypeBox = new ComboBox{Width = 100};
                    filterTypeBox.ItemsSource = Enum.GetValues<FilterType>();
                    filterTypeBox.SelectedIndex = filterType;
                    parameterPanel.Children.Add(filterTypeBox);

                    filterTypeBox.SelectionChanged += (_,_) =>{
                        filterType = filterTypeBox.SelectedIndex;
};
                    break;

                case Values.KernelSize:
                    parameterPanel.Children.Clear();
                    var kernelSizeBox = new TextBox{Width = 100, Text = kernelSize.ToString()};
                    parameterPanel.Children.Add(kernelSizeBox);

                    //Gets the value from the textbox
                    kernelSizeBox.TextChanged += (_,_) =>
                    {
                        if(int.TryParse(kernelSizeBox.Text, out int value))
                        {
                            //Makes sure that the kernelsize is odd by rounding up
                            if (value % 2 == 0) {
                                kernelSize = value + 1;
                                StatusText.Text = $"Value needs to be odd, value changed to {kernelSize}";
                                }
                            else{kernelSize = value;}
                        }
                    };
                    break;

                case Values.SigmaValue: 
                //should only be available when gaussian filter is selected
                    parameterPanel.Children.Clear();
                    if (filterType == 0){
                        var sigmaValueBox = new TextBox{Width = 100, Text = sigmaValue.ToString()};
                        parameterPanel.Children.Add(sigmaValueBox);
                        sigmaValueBox.TextChanged += (_,_) => {
                            if(float.TryParse(sigmaValueBox.Text, out float i))
                            {
                                if(i < 0)
                                {
                                    sigmaValue = Math.Abs(i);
                                    StatusText.Text = $"Negative value inputed, value changed to {sigmaValue}";
                                }
                                else{sigmaValue = i;}
                        }};
                    }
                    else{
                        //Makes value of sigma null to ensure it will not be used
                        var warning = new TextBlock{Text="UNAVAILABLE"};
                        parameterPanel.Children.Add(warning);}
                    break;

                case Values.Threshold:
                //this is for edge detecting
                    parameterPanel.Children.Clear();
                    var thresholdBox = new TextBox{Width = 100, Text = threshold.ToString()};
                    parameterPanel.Children.Add(thresholdBox);

                    thresholdBox.TextChanged += (_,_) =>{
                        if(int.TryParse(thresholdBox.Text, out int value)){
                            if(value > 255)
                            {
                                threshold = 255;
                                StatusText.Text = $"Value needs to be under 255, value changed to 255.";
                            }
                            else if (value < 0)
                            {
                                threshold = 0;
                                StatusText.Text = $"Value needs to be above 0, value changed to 0.";
                            }
                            else{
                                threshold = value;}};
                            };
                    break;

                case Values.HighThreshold:
                    parameterPanel.Children.Clear();
                    var highThresholdBox = new TextBox{Width = 100, Text = highThreshold.ToString()};
                    parameterPanel.Children.Add(highThresholdBox);

                    highThresholdBox.TextChanged += (_,_) =>
                    {
                        if(int.TryParse(highThresholdBox.Text, out int value))
                        {
                            if(value > 255 || value < 0)
                            {
                                value = Math.Clamp(value, 0, 255);
                                highThreshold = value;
                                StatusText.Text = $"Value outside of 0-255 value clamped to {value}";
                            }
                            else
                            {
                                highThreshold = value;
                            }
                        }
                        else
                        {
                            StatusText.Text = $"Please input an integer value between 0 and 255.";
                        }
                    };
                break;

                case Values.LowThreshold:
                    parameterPanel.Children.Clear();
                    var lowThresholdBox = new TextBox{Width = 100, Text = lowThreshold.ToString()};
                    parameterPanel.Children.Add(lowThresholdBox);

                    lowThresholdBox.TextChanged += (_,_) =>
                    {
                        if(int.TryParse(lowThresholdBox.Text, out int value))
                        {
                            if(value > 255 || value < 0)
                            {
                                value = Math.Clamp(value, 0, 255);
                                lowThreshold = value;
                                StatusText.Text = $"Value outside of 0-255 value clamped to {value}";
                            }
                            else
                            {
                                lowThreshold = value;
                            }
                        }
                        else
                        {
                            StatusText.Text = $"Please input an integer value between 0 and 255.";
                        }
                    };
                break;

                case Values.StructureElement:
                    parameterPanel.Children.Clear();
                    var strucElemBox = new TextBox{Width = 100, Text = strucElemSize.ToString()};
                    parameterPanel.Children.Add(strucElemBox);
                    strucElemBox.TextChanged += (_,_) =>
                    {
                        if(int.TryParse(strucElemBox.Text, out int value))
                        {
                            if (value % 2 == 0){
                                strucElemSize = value + 1;
                                StatusText.Text = $"Value needs to be odd, value changed to {strucElemSize}.";}
                            else{
                                strucElemSize = value;}
                        }
                        else{
                            StatusText.Text = $"Please input an odd integer value.";}
                    };
                    break;
            }
        }

        /// <summary>
        /// Dispatches the selected image processing operation on a background task
        /// to keep the UI responsive during heavy computations.
        /// </summary>
        private async void OnApply(object? sender, RoutedEventArgs e)
        {
            if (_loadedColorPixels == null)
            {
                StatusText.Text = "Please load an image first.";
                return;
            }

            if (OperationBox.SelectedItem is not ProcessingFunctions selected)
            {
                StatusText.Text = "Please select a valid operation.";
                return;
            }

            ApplyButton.IsEnabled = false;
            StatusText.Text = "Processing...";

            byte[,,] colorPixels = _loadedColorPixels;

            try
            {
                // Run computation and bitmap generation on a background task
                // to keep the UI dispatcher thread responsive during heavy operations.
                var (resultGray, resultBmp) = await Task.Run(() =>
                {
                    // Grayscale conversion happens here on every Apply so every
                    // operation always starts from the original loaded image, never chained
                    // from a previous Apply's result.
                    byte[,] gray = ConvertToGrayscale(colorPixels);

                    switch (selected)
                    {
                        case ProcessingFunctions.ConvertToGrayscale:
                            // Already fully working; gray already holds the grayscale
                            // conversion result at this point (computed above, before this switch),
                            // so nothing further is needed here.
                            break;
                        case ProcessingFunctions.InvertImage:
                            gray = InvertImage(gray);
                            break;
                        case ProcessingFunctions.AdjustContrast:
                            gray = AdjustContrast(gray);
                            break;
                        case ProcessingFunctions.ConvolveImage:
                            if(filterType == 0){
                            gray = ConvolveImage(gray, CreateGaussianFilter());}
                            else{
                                StatusText.Text = "WARNING! Median filter selected, image not processed.";}
                            break;
                        case ProcessingFunctions.MedianFilter:
                            if(filterType == 1){
                            gray = MedianFilter(gray);}
                            else{
                                StatusText.Text = "WARNING! Gaussian filter selected, image not processed.";}
                            break;
                        case ProcessingFunctions.EdgeMagnitude:
                        {
                            gray = EdgeMagnitude(gray, horizontalKernel, verticalKernel);
                            break;
                        }
                        case ProcessingFunctions.ThresholdImage:
                            gray = ThresholdImage(gray, threshold);
                            break;

                        case ProcessingFunctions.BinaryErodeImage:
                        {
                            bool[,] structElem = CreateStructureElement();
                            gray = BinaryErodeImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.BinaryDilateImage:
                        {
                            bool[,] structElem = CreateStructureElement();
                            gray = BinaryDilateImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.BinaryOpenImage:
                        {
                            bool[,] structElem = CreateStructureElement(); // Define this structuring element yourself
                            gray = BinaryOpenImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.BinaryCloseImage:
                        {
                            bool[,] structElem = CreateStructureElement(); // Define this structuring element yourself
                            gray = BinaryCloseImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.GrayscaleErodeImage:
                        {
                            int[,] grayStructElem = CreateGrayStructureElement(); 
                             // Define this structuring element yourself
                            gray = GrayscaleErodeImage(gray, grayStructElem);
                            break;
                        }

                        case ProcessingFunctions.GrayscaleDilateImage:
                        {
                            int[,] grayStructElem = CreateGrayStructureElement(); // Define this structuring element yourself
                            gray = GrayscaleDilateImage(gray, grayStructElem);
                            break;
                        }

                        case ProcessingFunctions.HistogramMatching:
                        {
                             if (_referenceColorPixels == null)
                                {
                                    StatusText.Text = "Please load a reference image first.";
                                    break;
                                }
                            byte[,] refGray = ConvertToGrayscale(_referenceColorPixels);
                            gray = HistogramMatching(gray, refGray);
                            break;
                        }

                        case ProcessingFunctions.CannyEdgeDetection:
                        {
                            gray = CannyEdgeDetection(gray);
                            break;
                        }
                        
                        case ProcessingFunctions.Task1:
                            gray = CreateTask1(gray, filterType, kernelSize, horizontalKernel, verticalKernel, threshold);
                            break;

                        case ProcessingFunctions.Task2:
                            gray = CreateTask2(gray);
                            break;
                        
                        case ProcessingFunctions.Task3:
                            gray = CreateTask3(gray);
                            break;
                        
                        case ProcessingFunctions.SequentialRegionLabeling:
                            gray = a2.SequentialRegionLabeling(gray);
                            gray = AdjustContrast(gray);
                            break;
                        
                        case ProcessingFunctions.HoughTransform:
                            gray = Assignment2.HoughTransform(gray, false);
                            break;
                        
                        //Howw to vis??
                        case ProcessingFunctions.HoughPeakFinding:
                            List<(int, int)> peaks = Assignment2.PeakFinding(Assignment2.HoughTransform(gray, false), 10);
                                                gray = Assignment2.BytePeaks(peaks, 256, 256);
                            break;
                        
                        case ProcessingFunctions.HoughLineSegments:
                            throw new NotSupportedException($"Operation '{selected}' is not yet implemented.");
                            break;

                        case ProcessingFunctions.Floodfill:
                            gray = Assignment2.SLRFloodfill(gray);
                            gray = AdjustContrast(gray);
                            break;


                        default:
                            throw new NotSupportedException($"Operation '{selected}' is not implemented in the OnApply switch.");
                    }

                    var bmp = ByteArrayToBitmap(gray);
                    return (gray, bmp);
                });

                bool runErosionAnalysis = (selected == ProcessingFunctions.Task2);
                _processedGray = resultGray;
                (ProcessedImage.Source as IDisposable)?.Dispose();
                ProcessedImage.Source = resultBmp;
                if (_foregroundPixels != 0)
                {
                    StatusText.Text = $"There are {_foregroundPixels} foreground pixels.";
                    _foregroundPixels = 0;
                }
                else if (runErosionAnalysis)
                {
                    int[,] structElem = CreateGrayStructureElement();
                    byte[,] Ci = GrayscaleErodeImage(resultGray, structElem);
                    Ci = GrayscaleErodeImage(resultGray, structElem); 
                    int D = CountDistinctValues(Ci); 
                    double E = CalculateAverageIntensity(Ci);
                    StatusText.Text = $"Erosion (D = {D} distinct values, E = {E:F2} avg intensity";
                }
                else
                {
                    StatusText.Text = $"Completed {selected}.";
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error applying {selected}: {ex.Message}";
            }
            finally
            {
                ApplyButton.IsEnabled = true;
            }
        }

        // ====================================================================
        // ==================== GIVEN (already implemented) ==================
        // ====================================================================

        /// <summary>
        /// Converts loaded color pixel data (<c>[x, y, channel]</c> where channel 0=R, 1=G, 2=B)
        /// to single-channel grayscale by averaging RGB values.
        /// </summary>
        /// <param name="colorPixels">The 3D array of color pixels extracted at load time.</param>
        /// <returns>A 2D array of grayscale byte intensities with values in [0, 255].</returns>
        private static byte[,] ConvertToGrayscale(byte[,,] colorPixels)
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
        private byte[,] InvertImage(byte[,] inputImage)
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
        private byte[,] AdjustContrast(byte[,] inputImage)
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
        private float[,] CreateGaussianFilter()
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
        private byte[,] ConvolveImage(byte[,] inputImage, float[,] filter)
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
        private byte[,] MedianFilter(byte[,] inputImage)
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
        private byte[,] EdgeMagnitude(
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
        private byte[,] ThresholdImage(byte[,] inputImage, int thresh)
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
        private byte[,] BinaryErodeImage(byte[,] inputImage, bool[,] structElem)
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
        private byte[,] BinaryDilateImage(byte[,] inputImage, bool[,] structElem)
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
        private byte[,] BinaryOpenImage(byte[,] inputImage, bool[,] structElem)
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
        private byte[,] BinaryCloseImage(byte[,] inputImage, bool[,] structElem)
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
        private byte[,] GrayscaleErodeImage(byte[,] inputImage, int[,] structElem)
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
        private byte[,] GrayscaleDilateImage(byte[,] inputImage, int[,] structElem)
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

        private byte[,] HistogramMatching(byte[,] inputImage, byte[,] referenceImage)
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

        private bool [,] CreateStructureElement()
        {
            bool [,] strucElement = new bool [strucElemSize, strucElemSize];
            for(int i = 0; i < strucElemSize; i++)
            for(int j = 0; j < strucElemSize; j++)
                {
                    strucElement[i,j] = true;
                }
            return strucElement;
        }
             private int [,] CreateGrayStructureElement()
        {
            int [,] strucElement = new int [strucElemSize, strucElemSize];
            for(int i = 0; i < strucElemSize; i++)
            for(int j = 0; j < strucElemSize; j++)
            {
                strucElement[i,j] = 100;
            }
            return strucElement;
        }

        
        private byte[,] CannyEdgeDetection(byte[,] inputImage)
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
        
        private byte[,] CreateTask1(byte[,] inputImage, int filterType, int kernelSize, sbyte[,] horizontalkernel, sbyte[,] verticalkernel, int threshold)
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

        private byte [,] CreateTask2(byte[,] inputImage)
        {
            byte [,] output = AdjustContrast(inputImage);
            int [,] strucElem = CreateGrayStructureElement();
            output = GrayscaleErodeImage(output, strucElem);
            return output;
        }
        
        private byte [,] CreateTask3(byte [,] inputImage)
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

        private int CountDistinctValues(byte[,] image)
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

        private double CalculateAverageIntensity(byte[,] image)
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


        // ====================================================================
        // ==================== IMAGE <-> BITMAP HELPERS (given) =============
        // ====================================================================

        /// <summary>
        /// Builds a displayable and savable <see cref="WriteableBitmap"/> from a grayscale byte[,] array
        /// (replicated into R, G, B; alpha fully opaque), using Avalonia's native imaging APIs.
        /// </summary>
        /// <param name="gray">The 2D grayscale byte array.</param>
        /// <returns>A displayable and savable <see cref="WriteableBitmap"/>.</returns>
        private WriteableBitmap ByteArrayToBitmap(byte[,] gray)
        {
            int w = gray.GetLength(0);
            int h = gray.GetLength(1);
            var size = new PixelSize(w, h);
            var bmp = new WriteableBitmap(
                size,
                new(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Opaque
            );

            using var fb = bmp.Lock();

            int totalBytes = fb.RowBytes * h;
            byte[] buffer = new byte[totalBytes];
            for (int y = 0; y < h; y++)
            {
                int rowStart = y * fb.RowBytes;
                for (int x = 0; x < w; x++)
                {
                    byte val = gray[x, y];
                    int idx = rowStart + x * 4;
                    buffer[idx + 0] = val; // R
                    buffer[idx + 1] = val; // G
                    buffer[idx + 2] = val; // B
                    buffer[idx + 3] = 255; // A (fully opaque)
                }
            }

            Marshal.Copy(buffer, 0, fb.Address, totalBytes);

            return bmp;
        }
    }
}