/*
 *  ARTrackable.cs
 *  artoolkitX for Unity
 *
 *  This file is part of artoolkitX for Unity.
 *
 *  artoolkitX for Unity is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU Lesser General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  artoolkitX for Unity is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with artoolkitX for Unity.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  As a special exception, the copyright holders of this library give you
 *  permission to link this library with independent modules to produce an
 *  executable, regardless of the license terms of these independent modules, and to
 *  copy and distribute the resulting executable under terms of your choice,
 *  provided that you also meet, for each linked independent module, the terms and
 *  conditions of the license of that module. An independent module is a module
 *  which is neither derived from nor based on this library. If you modify this
 *  library, you may extend this exception to your version of the library, but you
 *  are not obligated to do so. If you do not wish to do so, delete this exception
 *  statement from your version.
 *
 *  Copyright 2015 Daqri, LLC.
 *  Copyright 2010-2015 ARToolworks, Inc.
 *
 *  Author(s): Philip Lamb, Julian Looser
 *
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEngine;

/// <summary>
/// ARTrackable objects represent a native ARTrackable, even when the native artoolkitX is not
/// initialised.
/// To find markers from elsewhere in the Unity environment:
///   ARTrackable[] markers = FindObjectsOfType<ARTrackable>(); // (or FindObjectsOfType(typeof(ARTrackable)) as ARTrackable[]);
/// 
/// </summary>
/// 
[ExecuteInEditMode]
public class ARTrackable : MonoBehaviour
{
    public enum TrackableType
    {
        Square,              // A square template (pattern) marker.
        SquareBarcode,      // A square matrix (2D barcode) marker.
        Multimarker,        // Multiple square markers treated as a single marker.
        NFT,                // A legacy NFT marker.
        TwoD                // An artoolkitX 2D textured trackable.
    }

    public enum ARWTrackableOption : int
    {
        ARW_TRACKABLE_OPTION_FILTERED = 1,
        ARW_TRACKABLE_OPTION_FILTER_SAMPLE_RATE = 2,
        ARW_TRACKABLE_OPTION_FILTER_CUTOFF_FREQ = 3,
        ARW_TRACKABLE_OPTION_SQUARE_USE_CONT_POSE_ESTIMATION = 4,
        ARW_TRACKABLE_OPTION_SQUARE_CONFIDENCE = 5,
        ARW_TRACKABLE_OPTION_SQUARE_CONFIDENCE_CUTOFF = 6,
        ARW_TRACKABLE_OPTION_NFT_SCALE = 7
    }

    public readonly static Dictionary<TrackableType, string> TrackableTypeNames = new Dictionary<TrackableType, string>
    {
        {TrackableType.Square, "Single AR pattern"},
        {TrackableType.SquareBarcode, "Single AR barcode"},
        {TrackableType.Multimarker, "Multimarker AR configuration"},
        {TrackableType.NFT, "NFT dataset"},
        {TrackableType.TwoD, "2D image texture"}
    };

    private const string LogTag = "ARTrackable: ";

    // Quaternion to rotate from ART to Unity
    //public static Quaternion RotationCorrection = Quaternion.AngleAxis(90.0f, new Vector3(1.0f, 0.0f, 0.0f));

    // Value used when no underlying native ARTrackable is assigned
    public const int NO_ID = -1;

    [NonSerialized] private int uid = NO_ID;
    // Current Unique Identifier (UID) assigned to this marker.
    // UID is not serialized because its value is only meaningful during a specific run.
    public int UID
    {
        get
        {
            return uid;
        }
    }
    /// <summary>
    /// Will be set to true if a load failed.
    /// </summary>
    private bool loadError = false;

    [field: SerializeField]
    public TrackableType Type { get; private set; } = TrackableType.TwoD;

    public string Tag = ""; // This links this trackable with an ARTrackedObject or ARTrackedCamera in the scene.

    // 2D image trackables have an image filename and image width.
    [field: SerializeField]
    public string TwoDImageFile { get; private set; } = "";
    [field: SerializeField]
    public float TwoDImageWidth { get; private set; } = 1.0f;
    public void ConfigureAsTwoD(string imageFilePath, float imageWidth)
    {
        Type = TrackableType.TwoD;
        if (!string.IsNullOrEmpty(imageFilePath)) TwoDImageFile = imageFilePath;
        if (imageWidth > 0.0f) TwoDImageWidth = imageWidth;
        loadError = false;
        Load();
    }

    // If the marker is single square pattern, then it has a filename and pattern.
    // Once configured, the filename is just for display purposes.
    // Alternately, barcode markers have a user-selected ID.
    // Both types have width (of border).
    [field: SerializeField]
    public string PatternFileName { get; private set; } = "";
    [field: SerializeField]
    public string PatternContents { get; private set; } = ""; // Set by the editor.
    [field: SerializeField]
    public long BarcodeID { get; private set; } = 0;
    [field: SerializeField]
    public float PatternWidth { get; private set; } = 0.08f;
    public void ConfigureAsSquarePattern(string patternFilePath, float width)
    {
        Type = TrackableType.Square;
        if (!string.IsNullOrEmpty(patternFilePath))
        {
            PatternFileName = Path.GetFileName(patternFilePath);
            PatternContents = String.Join(" ", System.IO.File.ReadAllLines(patternFilePath));
        }
        if (width > 0.0f) PatternWidth = width;
        loadError = false;
        Load();
    }
    public void ConfigureAsSquareBarcode(long barcodeID, float width)
    {
        Type = TrackableType.SquareBarcode;
        if (barcodeID >= 0) BarcodeID = barcodeID;
        if (width > 0.0f) PatternWidth = width;
        loadError = false;
        Load();
    }

    // If the marker is multi, it just has a config filename.
    [field: SerializeField]
    public string MultiConfigFile { get; private set; } = "";
    public void ConfigureAsMultiSquare(string multiConfigFilePath)
    {
        Type = TrackableType.Multimarker;
        if (!string.IsNullOrEmpty(multiConfigFilePath)) MultiConfigFile = multiConfigFilePath;
        loadError = false;
        Load();
    }

    // NFT markers have a dataset pathname (less the extension).
    // Also, we need a list of the file extensions that make up an NFT dataset.
    [field: SerializeField]
    public string NFTDataName { get; private set; } = "";
#if !UNITY_METRO
    private readonly string[] NFTDataExts = { "iset", "fset", "fset3" };
#endif
    public void ConfigureAsNFT(string nftDataName)
    {
        Type = TrackableType.NFT;
        if (!string.IsNullOrEmpty(nftDataName)) NFTDataName = nftDataName;
        loadError = false;
        Load();
    }

    // Single markers and 2D planar surfaces have a single pattern, multi markers and NFT markers have one or more..
    private ARPattern[] patterns;

    // Private fields with accessors.
    // Trackable configuration options.
    [SerializeField]
    private bool currentUseContPoseEstimation = false;                        // Single marker only; whether continuous pose estimation should be used.
    [SerializeField]
    private bool currentFiltered = false;
    [SerializeField]
    private float currentFilterSampleRate = 30.0f;
    [SerializeField]
    private float currentFilterCutoffFreq = 15.0f;
    [SerializeField]
    private float currentNFTScale = 1.0f;                                    // NFT marker only; scale factor applied to marker size.

    [NonSerialized]
    public float NFTWidth; //> Once marker is loaded, this holds the width of the marker in Unity units.
    [NonSerialized]
    public float NFTHeight; //> Once marker is loaded, this holds the height of the marker in Unity units.

    // Realtime tracking information
    private bool visible = false;                                           // Trackable is visible or not
    private Matrix4x4 transformationMatrix;                                 // Full transformation matrix as a Unity matrix
//    private Quaternion rotation = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);   // Rotation corrected for Unity
//    private Vector3 position = new Vector3(0.0f, 0.0f, 0.0f);               // Position corrected for Unity
    
    private object loadLock = new object();

    /// <summary>
    /// Factory method to add a 2D image trackable to same GameObject as ARController.
    /// </summary>
    /// <param name="imageFilePath">Filesystem path, relative to StreamingAssets folder, of image file.</param>
    /// <param name="imageWidth">Width of the image in Unity units, default 1.0 = 1 metre.</param>
    /// <returns></returns>
    static ARTrackable Add2D(string imageFilePath, float imageWidth)
    {
        if (!ARController.Instance || ARController.Instance.PluginFunctions == null || !ARController.Instance.PluginFunctions.IsInited()) return null;
        ARTrackable t = ARController.Instance.gameObject.AddComponent<ARTrackable>();
        t.ConfigureAsTwoD(imageFilePath, imageWidth);
        return t;
    }

    /// <summary>
    /// actory method to add a square pattern marker trackable to same GameObject as ARController.
    /// </summary>
    /// <param name="patternFilePath">Filesystem path of pattern file.</param>
    /// <param name="patternWidth">Width of marker (measured on one side of border) in Unity units, default 0.08 = 80mm.</param>
    /// <returns></returns>
    static ARTrackable AddSquarePattern(string patternFilePath, float patternWidth)
    {
        if (!ARController.Instance || ARController.Instance.PluginFunctions == null || !ARController.Instance.PluginFunctions.IsInited()) return null;
        ARTrackable t = ARController.Instance.gameObject.AddComponent<ARTrackable>();
        t.ConfigureAsSquarePattern(patternFilePath, patternWidth);
        return t;
    }

    static ARTrackable AddSquareBarcode(long barcodeID, float patternWidth)
    {
        if (!ARController.Instance || ARController.Instance.PluginFunctions == null || !ARController.Instance.PluginFunctions.IsInited()) return null;
        ARTrackable t = ARController.Instance.gameObject.AddComponent<ARTrackable>();
        t.ConfigureAsSquareBarcode(barcodeID, patternWidth);
        return t;
    }

    static ARTrackable AddMultiSquare(string configFilePath)
    {
        if (!ARController.Instance || ARController.Instance.PluginFunctions == null || !ARController.Instance.PluginFunctions.IsInited()) return null;
        ARTrackable t = ARController.Instance.gameObject.AddComponent<ARTrackable>();
        t.ConfigureAsMultiSquare(configFilePath);
        return t;
    }

    static ARTrackable AddNFT(string dataFilename)
    {
        if (!ARController.Instance || ARController.Instance.PluginFunctions == null || !ARController.Instance.PluginFunctions.IsInited()) return null;
        ARTrackable t = ARController.Instance.gameObject.AddComponent<ARTrackable>();
        t.ConfigureAsNFT(dataFilename);
        return t;
    }

    public void OnDisable()
    {
        //ARController.Log(LogTag + "ARTrackable.OnDisable()");
        Unload();
    }

#if !UNITY_METRO
    private bool unpackStreamingAssetToCacheDir(string basename)
    {
        if (!File.Exists(System.IO.Path.Combine(Application.temporaryCachePath, basename))) {
            string file = System.IO.Path.Combine(Application.streamingAssetsPath, basename); // E.g. "jar:file://" + Application.dataPath + "!/assets/" + basename;
#pragma warning disable CS0618 // Keep using WWW for 'jar:' method support.
            WWW unpackerWWW = new WWW(file);
#pragma warning restore CS0618
            while (!unpackerWWW.isDone) { } // This will block in the webplayer. TODO: switch to co-routine.
            if (!string.IsNullOrEmpty(unpackerWWW.error)) {
                ARController.Log(LogTag + "Error unpacking '" + file + "'");
                return (false);
            }
            File.WriteAllBytes(System.IO.Path.Combine(Application.temporaryCachePath, basename), unpackerWWW.bytes); // 64MB limit on File.WriteAllBytes.
        }
        return (true);
    }
    #endif

    // Load the native ARTrackable structure(s) and set the UID.
    public void Load() 
    {
        lock (loadLock) {
            if (this.enabled == false) {
                return;
            }

            //ARController.Log(LogTag + "ARTrackable.Load()");
            if (UID != NO_ID) {
                return;
            }

            if (!ARController.Instance || ARController.Instance.PluginFunctions == null || !ARController.Instance.PluginFunctions.IsInited()) {
                // If arwInitialiseAR() has not yet been called, we can't load the native trackable yet.
                // ARController.InitialiseAR() will trigger this again when arwInitialiseAR() has been called.
                return;
            }

            // Work out the configuration string to pass to the native side.
            string dir = Application.streamingAssetsPath;
            string cfg = "";
            
            switch (Type) {

                case TrackableType.Square:
                    // Multiply width by 1000 to convert from metres to artoolkitX's millimetres.
                    cfg = "single_buffer;" + PatternWidth*1000.0f + ";buffer=" + PatternContents;
                    break;
                
                case TrackableType.SquareBarcode:
                    // Multiply width by 1000 to convert from metres to artoolkitX's millimetres.
                    cfg = "single_barcode;" + BarcodeID + ";" + PatternWidth*1000.0f;
                    break;
                
                case TrackableType.Multimarker:
                    #if !UNITY_METRO
                    if (dir.Contains("://")) {
                        // On Android, we need to unpack the StreamingAssets from the .jar file in which
                        // they're archived into the native file system.
                        dir = Application.temporaryCachePath;
                        if (!unpackStreamingAssetToCacheDir(MultiConfigFile)) {
                            dir = "";
                        } else {
                        
                            //string[] unpackFiles = getPatternFiles;
                            //foreach (string patternFile in patternFiles) {
                            //if (!unpackStreamingAssetToCacheDir(patternFile)) {
                            //    dir = "";
                            //    break;
                            //}
                        }
                    }
                    #endif
                    
                    if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(MultiConfigFile)) {
                        cfg = "multi;" + System.IO.Path.Combine(dir, MultiConfigFile);
                    }
                    break;

                
                case TrackableType.NFT:
                    #if !UNITY_METRO
                    if (dir.Contains("://")) {
                        // On Android, we need to unpack the StreamingAssets from the .jar file in which
                        // they're archived into the native file system.
                        dir = Application.temporaryCachePath;
                        foreach (string ext in NFTDataExts) {
                            string basename = NFTDataName + "." + ext;
                            if (!unpackStreamingAssetToCacheDir(basename)) {
                                dir = "";
                                break;
                            }
                        }
                    }
                    #endif
                
                    if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(NFTDataName)) {
                        cfg = "nft;" + System.IO.Path.Combine(dir, NFTDataName);
                    }
                    break;

            case TrackableType.TwoD:
                #if !UNITY_METRO
                if (dir.Contains("://")) {
                    // On Android, we need to unpack the StreamingAssets from the .jar file in which
                    // they're archived into the native file system.
                    dir = Application.temporaryCachePath;
                    if (!unpackStreamingAssetToCacheDir(TwoDImageFile)) {
                        dir = "";
                    }
                }
                #endif

                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(TwoDImageFile)) {
                    cfg = "2d;" + System.IO.Path.Combine(dir, TwoDImageFile) + ";" + TwoDImageWidth * 1000.0f;
                }
                break;

                default:
                    // Unknown marker type?
                    break;

            }
            
            // If a valid config. could be assembled, get the native side to process it, and assign the resulting ARTrackable UID.
            if (!string.IsNullOrEmpty(cfg)) {
                uid = ARController.Instance.PluginFunctions.arwAddTrackable(cfg);
                if (UID == NO_ID) {
                    ARController.Log(LogTag + "Error loading marker.");
                    loadError = true;
                } else {
                    loadError = false;
                    // Trackable loaded. Do any additional configuration.
                    //ARController.Log("Added marker with cfg='" + cfg + "'");

                    if (Type == TrackableType.Square || Type == TrackableType.SquareBarcode) UseContPoseEstimation = currentUseContPoseEstimation;
                    Filtered = currentFiltered;
                    FilterSampleRate = currentFilterSampleRate;
                    FilterCutoffFreq = currentFilterCutoffFreq;

                    // Retrieve any required information from the configured native ARTrackable.
                    if (Type == TrackableType.NFT || Type == TrackableType.TwoD) {
                        if (Type == TrackableType.NFT) NFTScale = currentNFTScale;

                        int imageSizeX, imageSizeY;
                        ARController.Instance.PluginFunctions.arwGetTrackablePatternConfig(UID, 0, null, out NFTWidth, out NFTHeight, out imageSizeX, out imageSizeY);
                        NFTWidth *= 0.001f;
                        NFTHeight *= 0.001f;
                        //ARController.Log("Got NFTWidth=" + NFTWidth + ", NFTHeight=" + NFTHeight + ".");
                    
                    } else {

                        // Create array of patterns. A single marker will have array length 1.
                        int numPatterns = ARController.Instance.PluginFunctions.arwGetTrackablePatternCount(UID);
                        //ARController.Log("Trackable with UID=" + UID + " has " + numPatterns + " patterns.");
                        if (numPatterns > 0) {
                            patterns = new ARPattern[numPatterns];
                            for (int i = 0; i < numPatterns; i++) {
                                patterns[i] = new ARPattern(UID, i);
                            }
                        }

                    }
                }
            }
        }
    }

    // We use Update() here, but be aware that unless ARController has been configured to
    // execute first (Unity Editor->Edit->Project Settings->Script Execution Order) then
    // state produced by this update may lag by one frame.
    void Update()
    {
        // Only query visibility if we are running in the Player.
        if (!Application.isPlaying)
        {
            return;
        }

        lock (loadLock)
        {
            bool v = false;
            //ARController.Log(LogTag + "ARTrackable.Update()");

            if (ARController.Instance && ARController.Instance.PluginFunctions != null && ARController.Instance.PluginFunctions.IsInited()) {

                // Lazy loading, provided we didn't already try to load and get an error.
                if (UID == NO_ID && !loadError)
                {
                    Load();
                }
                if (UID != NO_ID)
                {
                    float[] matrixRawArray = new float[16];
                    v = ARController.Instance.PluginFunctions.arwQueryTrackableVisibilityAndTransformation(UID, matrixRawArray);
                    //ARController.Log(LogTag + "ARTrackable.Update() UID=" + UID + ", visible=" + v);

                    if (v)
                    {
                        matrixRawArray[12] *= 0.001f; // Scale the position from artoolkitX units (mm) into Unity units (m).
                        matrixRawArray[13] *= 0.001f;
                        matrixRawArray[14] *= 0.001f;

                        Matrix4x4 matrixRaw = ARUtilityFunctions.MatrixFromFloatArray(matrixRawArray);
                        //.Log("arwQueryTrackableTransformation(" + UID + ") got matrix: [" + Environment.NewLine + matrixRaw.ToString("F3").Trim() + "]");

                        // artoolkitX uses right-hand coordinate system where the marker lies in x-y plane with right in direction of +x,
                        // up in direction of +y, and forward (towards viewer) in direction of +z.
                        // Need to convert to Unity's left-hand coordinate system where marker lies in x-y plane with right in direction of +x,
                        // up in direction of +y, and forward (towards viewer) in direction of -z.
                        transformationMatrix = ARUtilityFunctions.LHMatrixFromRHMatrix(matrixRaw);
                    }
                }
            }
            visible = v;
            return;
        }
    }
    
    // Unload any native ARTrackable structures, and clear the UID.
    public void Unload()
    {
        lock (loadLock) {
            //ARController.Log(LogTag + "ARTrackable.Unload()");
            
            if (UID == NO_ID) {
                return;
            }
            
            // Remove the native trackable, unless arwShutdownAR() has already been called (as it will already have been removed.)
            if (ARController.Instance && ARController.Instance.PluginFunctions != null && ARController.Instance.PluginFunctions.IsInited()) {
                ARController.Instance.PluginFunctions.arwRemoveTrackable(UID);
            }

            uid = NO_ID;
            loadError = false;
            patterns = null; // Delete the patterns too.
        }
    }
    
    public Matrix4x4 TransformationMatrix
    {
        get
        {                
            return transformationMatrix;
        }
    }

//    public Vector3 Position
//    {
//        get
//        {
//            return position;
//        }
//    }
//
//    public Quaternion Rotation
//    {
//        get
//        {
//            return rotation;
//        }
//    }

    public bool Visible
    {
        get
        {
            return visible;
        }
    }


    public ARPattern[] Patterns
    {
        get
        {
            return patterns;
        }
    }

    public bool Filtered
    {
        get
        {
            return currentFiltered; // Serialised.
        }

        set
        {
            currentFiltered = value;
            lock (loadLock) {
                if (UID != NO_ID) {
                    ARController.Instance.PluginFunctions.arwSetTrackableOptionBool(UID, (int)ARWTrackableOption.ARW_TRACKABLE_OPTION_FILTERED, value);
                }
            }
        }
    }

    public float FilterSampleRate
    {
        get
        {
            return currentFilterSampleRate; // Serialised.
        }

        set
        {
            currentFilterSampleRate = value;
            lock (loadLock) {
                if (UID != NO_ID) {
                    ARController.Instance.PluginFunctions.arwSetTrackableOptionFloat(UID, (int)ARWTrackableOption.ARW_TRACKABLE_OPTION_FILTER_SAMPLE_RATE, value);
                }
            }
        }
    }

    public float FilterCutoffFreq
    {
        get
        {
            return currentFilterCutoffFreq; // Serialised.
        }

        set
        {
            currentFilterCutoffFreq = value;
            lock (loadLock) {
                if (UID != NO_ID) {
                    ARController.Instance.PluginFunctions.arwSetTrackableOptionFloat(UID, (int)ARWTrackableOption.ARW_TRACKABLE_OPTION_FILTER_CUTOFF_FREQ, value);
                }
            }
        }
    }

    public bool UseContPoseEstimation
    {
        get
        {
            return currentUseContPoseEstimation; // Serialised.
        }

        set
        {
            currentUseContPoseEstimation = value;
            lock (loadLock) {
                if (UID != NO_ID && (Type == TrackableType.Square || Type == TrackableType.SquareBarcode)) {
                    ARController.Instance.PluginFunctions.arwSetTrackableOptionBool(UID, (int)ARWTrackableOption.ARW_TRACKABLE_OPTION_SQUARE_USE_CONT_POSE_ESTIMATION, value);
                }
            }
        }
    }

    public float NFTScale
    {
        get
        {
            return currentNFTScale; // Serialised.
        }
        
        set
        {
            currentNFTScale = value;
            lock (loadLock) {
                if (UID != NO_ID && (Type == TrackableType.NFT)) {
                    ARController.Instance.PluginFunctions.arwSetTrackableOptionFloat(UID, (int)ARWTrackableOption.ARW_TRACKABLE_OPTION_NFT_SCALE, value);
                }
            }
        }
    }
    

}
