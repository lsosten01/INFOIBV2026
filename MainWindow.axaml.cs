using System.Runtime.InteropServices;
using Avalonia;
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
        private byte[,]? _processedGray; // Processed grayscale values (nullable)

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
        }

        private enum Task1
        {
            FilterType,
            KernelSize,
            SigmaValue,
            Threshold,
        }

        private enum FilterType
        {
            Gaussian,
            Median,
        }

        private int kernelSize = 9;
        private float sigmaValue = 3.0f;
        private float threshold = 110;
        private int filterType = 0;

        public MainWindow()
        {
            InitializeComponent();
            var task1Box = this.Get<ComboBox>("Task1Box");

            OperationBox.ItemsSource = Enum.GetValues<ProcessingFunctions>();
            OperationBox.SelectedIndex = 0; // Select first item by default

            task1Box.ItemsSource = Enum.GetValues<Task1>();
            task1Box.SelectedIndex = 0;
            
        }

        /// <summary>
        /// Opens a file picker dialog, loads the selected image, and extracts its color pixel data.
        /// </summary>
        private async void OnLoadImage(object? sender, RoutedEventArgs e)
        {
            if (!StorageProvider.CanOpen)
            {
                StatusText.Text = "Opening files is not supported on this system.";
                return;
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
                    return;

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

                _loadedColorPixels = new byte[width, height, 3];
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
                            _loadedColorPixels[x, y, 0] = buffer[idx + 0];
                            _loadedColorPixels[x, y, 1] = buffer[idx + 1];
                            _loadedColorPixels[x, y, 2] = buffer[idx + 2];
                        }
                    }
                }

                _processedGray = null;
                (OriginalImage.Source as IDisposable)?.Dispose();
                OriginalImage.Source = _loadedBitmap;
                (ProcessedImage.Source as IDisposable)?.Dispose();
                ProcessedImage.Source = null;
                StatusText.Text = $"Loaded image ({width} \u00d7 {height} px).";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load image: {ex.Message}";
            }
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

        private async void OnTask1Changed(object? sender, SelectionChangedEventArgs e)
        {
            var parameterPanel = this.Get<StackPanel>("ParameterPanel");
            var selected = Task1Box. SelectedItem;

            switch (selected)
            {
                //put the numbers where they should be
                case Task1.FilterType:
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

                case Task1.KernelSize:
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
                                }
                            else{kernelSize = value;}
                        }
                    };
                    break;

                case Task1.SigmaValue: 
                //should only be available when gaussian filter is selected
                    parameterPanel.Children.Clear();
                    if (filterType == 0){
                        var sigmaValueBox = new TextBox{Width = 100, Text = sigmaValue.ToString()};
                        parameterPanel.Children.Add(sigmaValueBox);
                        sigmaValueBox.TextChanged += (_,_) => {
                            if(float.TryParse(sigmaValueBox.Text, out float i)){
                                sigmaValue = i;}};
                    }
                    else{
                        //Makes value of sigma null to ensure it will not be used
                        var warning = new TextBlock{Text="UNAVAILABLE"};
                        sigmaValue = -1;
                        parameterPanel.Children.Add(warning);}
                    break;

                case Task1.Threshold:
                //this is for edge detecting
                    parameterPanel.Children.Clear();
                    var thresholdBox = new TextBox{Width = 100, Text = threshold.ToString()};
                    parameterPanel.Children.Add(thresholdBox);

                    thresholdBox.TextChanged += (_,_) =>{
                        if(float.TryParse(thresholdBox.Text, out float value)){
                            if(threshold > 255)
                            {
                                threshold = 255;
                            }
                            if (threshold < 0)
                            {
                                threshold = 0;
                            }
                            else{
                                threshold = value;}};
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
                            gray = EdgeMagnitude(gray, horizontalKernel, verticalKernel);
                            break;
                        }
                        case ProcessingFunctions.ThresholdImage:
                            gray = ThresholdImage(gray);
                            break;

                        case ProcessingFunctions.BinaryErodeImage:
                        {
                            bool[,] structElem =
                            {
                                {true, true, true},
                                {true, true, true},
                                {true, true, true}
                            }; // Define this structuring element yourself
                            gray = BinaryErodeImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.BinaryDilateImage:
                        {
                            bool[,] structElem = {
                                {true, true, true},
                                {true, true, true},
                                {true, true, true}
                            }; // Define this structuring element yourself
                            gray = BinaryDilateImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.BinaryOpenImage:
                        {
                            bool[,] structElem = null; // Define this structuring element yourself
                            gray = BinaryOpenImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.BinaryCloseImage:
                        {
                            bool[,] structElem = null; // Define this structuring element yourself
                            gray = BinaryCloseImage(gray, structElem);
                            break;
                        }

                        case ProcessingFunctions.GrayscaleErodeImage:
                        {
                            int[,] grayStructElem =
                            {
                                {100, 100, 100},
                                {100, 100, 100},
                                {100, 100, 100}
                            }; // Define this structuring element yourself
                            gray = GrayscaleErodeImage(gray, grayStructElem);
                            break;
                        }

                        case ProcessingFunctions.GrayscaleDilateImage:
                        {
                            int[,] grayStructElem = {
                                {100, 100, 100},
                                {100, 100, 100},
                                {100, 100, 100}
                            }; // Define this structuring element yourself
                            gray = GrayscaleDilateImage(gray, grayStructElem);
                            break;
                        }

                        default:
                            throw new NotSupportedException($"Operation '{selected}' is not implemented in the OnApply switch.");
                    }

                    var bmp = ByteArrayToBitmap(gray);
                    return (gray, bmp);
                });

                _processedGray = resultGray;
                (ProcessedImage.Source as IDisposable)?.Dispose();
                ProcessedImage.Source = resultBmp;
                StatusText.Text = $"Completed {selected}.";
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
            
            // TODO: add your functionality and checks, think about border handling and type conversion
            
            //image loop
            for(int u = 0; u < iW; u++)
            for(int v = 0; v < iH; v++)
            {
                float sum = 0;
                
                //loop over kernel positions based on (0,0) center
                for(int i = -centerX; i < centerX; i++)
                for(int j = -centerY; j < centerY; j++)
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
                for(int i = -center; i < center; i++)
                for(int j = -center; j < center; j++)
                {
                    int xOffset = u + i;
                    int yOffset = v + j;
                    //TO DO: there is a black line currently at the top fix this before turn in
                    //If pixels are out of bound they do not get added to the list
                    if (xOffset >= 0 && xOffset < iW)
                            {
                                if(yOffset >= 0 && yOffset < iH)
                                {
                                    byte kernelByte = inputImage[xOffset,yOffset];
                                    kernelList.Add(kernelByte);
                                }
                                else{break;}
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
                    int xOffset = u + i;
                    int yOffset = v + j;

                    //perhaps this is a bit ass for border handling hmm
                    if (xOffset >= 0 && xOffset < iW)
                    if(yOffset >= 0 && yOffset < iH)
                        {
                            dx += horizontalKernel[i + center,j + center] * inputImage[xOffset, yOffset];
                            dy += verticalKernel[i + center,j + center] * inputImage[xOffset, yOffset];
                        }
                    else
                        {
                            dx += horizontalKernel[i + center,j + center] * inputImage[u, v];
                            dy += verticalKernel[i + center,j + center] * inputImage[u, v];
                        }
                    
                }
                //TO DO: this also fix the comments man
                //dx /= 8;
                //dy /= 8;
                int magnitude = Convert.ToInt32(Math.Round(Math.Sqrt(dx * dx + dy * dy)));
                resultImage[u, v] = (byte)Math.Clamp(magnitude, 0, 255);
            }

            return resultImage;
        }

        /// <summary>
        /// Thresholds a grayscale image into a binary representation based on a cutoff value.
        /// </summary>
        /// <param name="inputImage">The 2D input grayscale image.</param>
        /// <param name="threshold">Intensity threshold cutoff value in [0, 255].</param>
        /// <returns>A binary image represented as byte intensities (e.g. 0 and 255).</returns>
        private byte[,] ThresholdImage(byte[,] inputImage)
        {
            // create temporary grayscale image
            int w = inputImage.GetLength(0);
            int h = inputImage.GetLength(1);
            
            byte[,] threshImage = new byte[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                int val = inputImage[x, y];
                if (val <= threshold)
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
                    bool overlap = false;
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
                                        overlap = true;
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
        /// Performs morphological binary opening (erosion followed by dilation).
        /// </summary>
        /// <param name="inputImage">The binary input image.</param>
        /// <param name="structElem">2D boolean structuring element (true = foreground).</param>
        /// <returns>The opened binary image.</returns>
        private byte[,] BinaryOpenImage(byte[,] inputImage, bool[,] structElem)
        {
            byte[,] output = new byte[inputImage.GetLength(0), inputImage.GetLength(1)];
            // TODO: implement binary opening
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
            // TODO: implement binary closing
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