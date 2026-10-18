using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Xml;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
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
        private static double[,] _gradientDirection;
        private static int _foregroundPixels = 0;

        Assignment1 a1 = new Assignment1();
        Assignment2 a2 = new Assignment2();

        static sbyte[,] horizontalKernel =
                                {
                                    {-1, 0, 1},
                                    {-2, 0, 2},
                                    {-1, 0, 1}
                                }; 
        static sbyte[,] verticalKernel =
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
            Floodfill,
            RegionContours,
            BinaryPipeline
        }

        private enum Values
        {
            FilterType,
            KernelSize,
            SigmaValue,
            Threshold,
            HighThreshold,
            LowThreshold,
            StructureElement,
            isBinary
        }

        private enum FilterType
        {
            Gaussian,
            Median,
        }

        private enum IsBinary
        {
            False,
            True
        }

        private static int kernelSize = 9;
        private static float sigmaValue = 3.0f;
        private static int threshold = 110;
        private static int filterType = 0;
        private static int strucElemSize = 3;
        private static int highThreshold = 110;
        private static int lowThreshold = 50;
        private static int isBinary = 0;

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

                using var bmp = a1.ByteArrayToBitmap(_processedGray);
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

                case Values.isBinary:
                    parameterPanel.Children.Clear();
                    //Creates a combobox
                    var isBinaryBox = new ComboBox{Width = 100};
                    isBinaryBox.ItemsSource = Enum.GetValues<IsBinary>();
                    isBinaryBox.SelectedIndex = isBinary;
                    parameterPanel.Children.Add(isBinaryBox);

                    isBinaryBox.SelectionChanged += (_,_) =>{
                        isBinary = isBinaryBox.SelectedIndex;};
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
                    byte[,] gray = a1.ConvertToGrayscale(colorPixels);

                    switch (selected)
                    {
                        case ProcessingFunctions.ConvertToGrayscale:
                            // Already fully working; gray already holds the grayscale
                            // conversion result at this point (computed above, before this switch),
                            // so nothing further is needed here.
                            break;
                        case ProcessingFunctions.InvertImage:
                            gray = a1.InvertImage(gray);
                            break;
                        case ProcessingFunctions.AdjustContrast:
                            gray = a1.AdjustContrast(gray);
                            break;
                        case ProcessingFunctions.ConvolveImage:
                            if(filterType == 0){
                            gray = a1.ConvolveImage(gray, a1.CreateGaussianFilter());}
                            else{
                                StatusText.Text = "WARNING! Median filter selected, image not processed.";}
                            break;
                        case ProcessingFunctions.MedianFilter:
                            if(filterType == 1){
                            gray = a1.MedianFilter(gray);}
                            else{
                                StatusText.Text = "WARNING! Gaussian filter selected, image not processed.";}
                            break;
                        case ProcessingFunctions.EdgeMagnitude:
                        {
                            gray = a1.EdgeMagnitude(gray, horizontalKernel, verticalKernel);
                            break;
                        }
                        case ProcessingFunctions.ThresholdImage:
                            gray = a1.ThresholdImage(gray, threshold);
                            break;

                        case ProcessingFunctions.BinaryErodeImage:
                        {
                            bool[,] structElem = a1.CreateStructureElement();
                            gray = a1.BinaryErodeImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.BinaryDilateImage:
                        {
                            bool[,] structElem = a1.CreateStructureElement();
                            gray = a1.BinaryDilateImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.BinaryOpenImage:
                        {
                            bool[,] structElem = a1.CreateStructureElement(); // Define this structuring element yourself
                            gray = a1.BinaryOpenImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.BinaryCloseImage:
                        {
                            bool[,] structElem = a1.CreateStructureElement(); // Define this structuring element yourself
                            gray = a1.BinaryCloseImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.GrayscaleErodeImage:
                        {
                            int[,] grayStructElem = a1.CreateGrayStructureElement(); 
                             // Define this structuring element yourself
                            gray = a1.GrayscaleErodeImage(gray, grayStructElem);
                            break;
                        }

                        case ProcessingFunctions.GrayscaleDilateImage:
                        {
                            int[,] grayStructElem = a1.CreateGrayStructureElement(); // Define this structuring element yourself
                            gray = a1.GrayscaleDilateImage(gray, grayStructElem);
                            break;
                        }

                        case ProcessingFunctions.HistogramMatching:
                        {
                             if (_referenceColorPixels == null)
                                {
                                    StatusText.Text = "Please load a reference image first.";
                                    break;
                                }
                            byte[,] refGray = a1.ConvertToGrayscale(_referenceColorPixels);
                            gray = a1.HistogramMatching(gray, refGray);
                            break;
                        }

                        case ProcessingFunctions.CannyEdgeDetection:
                        {
                            gray = a1.CannyEdgeDetection(gray);
                            break;
                        }
                        
                        case ProcessingFunctions.Task1:
                            gray = a1.CreateTask1(gray, filterType, kernelSize, horizontalKernel, verticalKernel, threshold);
                            break;

                        case ProcessingFunctions.Task2:
                            gray = a1.CreateTask2(gray);
                            break;
                        
                        case ProcessingFunctions.Task3:
                            gray = a1.CreateTask3(gray);
                            break;
                        
                        case ProcessingFunctions.SequentialRegionLabeling:
                            gray = a2.SequentialRegionLabeling(gray);
                            gray = a1.AdjustContrast(gray);
                            break;
                        
                        case ProcessingFunctions.HoughTransform:
                            //gray = a2.HoughTransform(gray, isBinary);
                            break;
                        
                        //Howw to vis??
                        case ProcessingFunctions.HoughPeakFinding:
                            List<(int, int)> peaks = Assignment2.PeakFinding(Assignment2.HoughTransform(gray, isBinary), 10);
                                                gray = Assignment2.BytePeaks(peaks, 256, 256);
                            break;
                        
                        case ProcessingFunctions.HoughLineSegments:
                            throw new NotSupportedException($"Operation '{selected}' is not yet implemented.");
                            break;

                        case ProcessingFunctions.Floodfill:
                            //throw new NotSupportedException($"Operation '{selected}' is currently unoptimised and crashes the application.");
                            //to do
                            gray = Assignment2.SLRFloodfill(gray);
                            gray = a1.AdjustContrast(gray);
                            break;
                            

                        case ProcessingFunctions.RegionContours:
                            gray = a2.RegionContours(gray);
                            break;

                        case ProcessingFunctions.BinaryPipeline:
                            //As the function CannyEdgeDetection gives a binary edge map this function will be used
                            //(this needs to be made clear in the report) 
                            //(plus in the report we have now two different thresholds)
                            gray = a1.CannyEdgeDetection(gray);
                            gray = Assignment2.HoughTransform(gray);
                            //to do and now we need to add the red lines and suchhhh
                            break;

                        default:
                            throw new NotSupportedException($"Operation '{selected}' is not implemented in the OnApply switch.");
                    }

                    var bmp = a1.ByteArrayToBitmap(gray);
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
                    int[,] structElem = a1.CreateGrayStructureElement();
                    byte[,] Ci = a1.GrayscaleErodeImage(resultGray, structElem);
                    Ci = a1.GrayscaleErodeImage(resultGray, structElem); 
                    int D = a1.CountDistinctValues(Ci); 
                    double E = a1.CalculateAverageIntensity(Ci);
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

        
    }
}