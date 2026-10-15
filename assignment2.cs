using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Tmds.DBus.Protocol;


namespace ImageApp
{
    public partial class MainWindow : Window
    {

    }

    //here comes the sunshine
    
    //for sequential region labeling several steps need to be done
    //

    public byte [,] SequentialRegionLabeling(byte [,] inputImage)
        {
            int iW = inputImage.GetLength(0);
            int iH = inputImage.GetLength(1);

            (int i, int j)[] neighbours =
            {
              (-1, -1),
              (-1, 0),
              (-1, 1),
              (0, -1)  
            };

            byte [,] labeledImage = new byte [iW, iH];

            //The label value that keeps track of which label value we are on
            byte label = 1;
            
            //First pass
            for(int u = 0; u < iW; u++)
            for(int v = 0; v < iH; v++)
                {
                    if(inputImage[u,v] == 255)
                    {
                        //fix this this is a tempvalue
                        byte tempLabel = 255;
                        //Checks the neighbouring pixels
                        foreach((int i, int j) in neighbours)
                            {
                                int uOffset = Math.Clamp(u + i,0, iW -1);
                                int vOffset = Math.Clamp(v + j, 0, iH -1);
                                byte labelVal = labeledImage[uOffset, vOffset];

                                //Sees if there is a lower label value in one of the neighbouring pixels
                                if(labelVal > 0 && labelVal < tempLabel)
                                {
                                    tempLabel = labelVal;
                                }
                            }
                        
                        //If no neighbouring label was found create a new label
                        if(tempLabel == 255)
                        {
                            labeledImage[u,v] = label;
                            label++;
                            //not above 254 values but eh (for now)
                        }
                        else
                        {
                            labeledImage[u,v] = tempLabel;
                        }
                    }
                }

            labeledImage = ResolveCollisions(labeledImage, iW, iH);

            //now we need to do contrast adjustment
            
            return labeledImage;
        }

    private byte [,] ResolveCollisions(byte[,] labeledImage, int iW, int iH)
        {
            //Second pass this needs to go a bit differently
            //because we need to find all the labels that are connected
            Dictionary<byte, byte> labelPairs = new Dictionary<byte, byte>();

            for(int u = 0; u < iW; u++)
            for(int v = 0; v < iH; v++)
                {
                    if (labeledImage[u,v] > 0)
                    {
                        for(int i = -1; i <= 1; i++)
                        for(int j = -1; j <= 1; j++)
                            {
                                int uOffset = Math.Clamp(u + i,0, iW -1);
                                int vOffset = Math.Clamp(v + j, 0, iH -1);

                                byte neighbour = labeledImage[uOffset, vOffset];
                                byte current = labeledImage[u,v];
                                if(current != neighbour && neighbour > 0)
                                {
                                    //this is still ass but less so so eh
                                    if (neighbour < current)
                                    {   
                                        labelPairs = UpdateDictionary(labelPairs, neighbour, current);
                                    }
                                    else
                                    {
                                        labelPairs = UpdateDictionary(labelPairs, current, neighbour);
                                    }
                                }
                            }
                    }
                }

            //update the labeled image
            for(int u = 0; u < iW; u++)
            for(int v = 0; v < iH; v++)
                {
                    if(labelPairs.ContainsKey(labeledImage[u,v])){
                        labeledImage[u,v] = labelPairs[labeledImage[u,v]];
                    }
                    else
                    {
                        labeledImage[u,v] = 0;
                    }
                }
            
            return labeledImage;
        }

        private static Dictionary<byte, byte> UpdateDictionary (Dictionary<byte, byte> labelPairs, byte lowerVal, byte higherVal)
        {
            //Adds the current lowest values if they dont exist in the dictionary yet
            //strilll better way jsdfoh skdjfhsdlkj fhlskdjhgdlkfgzsjhuldkfzgh
            //lets just think its ok for now lol
            //https://stackoverflow.com/questions/46383172/disjoint-set-implementation-in-c-sharp
            //https://www.geeksforgeeks.org/dsa/introduction-to-disjoint-set-data-structure-or-union-find-algorithm/
            if(!labelPairs.ContainsKey(lowerVal)){
                labelPairs.Add(lowerVal, lowerVal);}
            else if(labelPairs[lowerVal] > lowerVal)
            {
                labelPairs[labelPairs[lowerVal]] = lowerVal;
                labelPairs[lowerVal] = lowerVal;
            }
            else
            {
                labelPairs[higherVal] = labelPairs[lowerVal];                              
            }


            if(!labelPairs.ContainsKey(higherVal)){
                labelPairs.Add(higherVal, lowerVal);
            }
            else if(labelPairs[higherVal] > lowerVal)
            {
                labelPairs[labelPairs[higherVal]] = lowerVal;
                labelPairs[higherVal] = lowerVal;
            }
            else
            {
                labelPairs[lowerVal] = labelPairs[higherVal];
            }


            return labelPairs;
        }
}