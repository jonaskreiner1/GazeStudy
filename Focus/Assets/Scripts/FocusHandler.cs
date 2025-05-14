using UnityEngine;
using System.IO;
using System;
using System.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Beautify.Universal;
using System.Collections.Generic;
using UnityEditor;
using System.Globalization;

public class FocusHandler : MonoBehaviour
{

    // Runtime Access
    public int FocusMode = 1;
    public int StudyID = 12;
    public float studyStartTimer = 0f;  // Timer variable
    public float studyStartTime = 40f;  // Timer duration to start the study (in seconds)
    public int currentIndex = -1;

    // Aligning Canvas and Cursor
    public GameObject referencedCamera;  // The camera representing the user's gaze /CenterEyeAnchor  public GameObject canvas;
    public GameObject canvas;           // The Canvas that follows the camera
    public GameObject cursor; // The cursor that should follow the gaze direction
    public GameObject centerMark;           // Center Point
    public float zOffsetCanvasToCamera = 2300f;


    // EyeGaze
    public OVREyeGaze eyeGaze; // Assign Script where EyeGaze is attached to this
    public float smoothing = 10f;
    public float gazeXOffset = 0;
    public float gazeYOffset = 0;
    public float scaleX = 1;
    public float scaleY = 1;
    public int distanceToPlane;

    // Calibration Eye Gaze
    public GameObject CalibrationParent; // Reference to the parent object
    private int calibrationIndex = 0;
    private Transform[] children;
    public bool isCalibrating = false;
    private Vector2[] rawGazeData = new Vector2[9];   // Stores uncalibrated gaze points
    private Vector2[] targetPoints = new Vector2[9];  // Stores known screen positions

    // Head Gaze
    public Transform headTransform; // Assign this to the OVRCameraRig's centerEyeAnchor

    // Filepath
    string filePath;

    // Study Process Variables
    private float startTime;
    private bool isTiming = false;
    public GameObject targetObject;
    public List<Material> materialPool;
    public bool studyStarted = false;
    private int studyStartSequence = 0;
    public AudioClip studyStartSound; // The audio clip to play when the study starts
    private AudioSource audioSource; // Reference to the AudioSource component
    public float focusDistance;
    public int focusError;
    public int targetImg;
    private bool resettingFocus = false;

    // Calculate Target Distance for Focus
    [System.Serializable]
    public struct ImageCoord
    {
        public int x;
        public int y;

        public ImageCoord(int x, int y)
        {
            this.x = x * 400 / 315; // Calculate from 3150x3150 to 4000 x 4000 space
            this.y = y * 400 / 315;
        }
    }
    public ImageCoord[] FocusTarget = new ImageCoord[21]; // index 0 is unused
    public float tan;


    void Start()
    {

        tan = Mathf.Tan(10f * Mathf.Deg2Rad);
        LoadFocusTargets(); // Load all desired focus targets
        LoadShuffledMaterials(); // Load All Scene Images to Material Pool randomized
        CalibrateEyeGaze(); // Calibrate Eyetracking

        audioSource = gameObject.AddComponent<AudioSource>(); // Configure Audio Source for Study Start Signal
        resetFocusDistanceToCenter(2f); // Move Focus to Center

    }

    void Update()
    {
        if (isCalibrating)
        {
            if (Input.GetKeyDown(KeyCode.Space)) RecordCalibrationPoint();
        }


        else
        {
            studyStartMethod(); // Timers and setup for study


            // Handle Spacebar Triggered (Activation)
            if (Input.GetKeyDown(KeyCode.Space) && !resettingFocus && studyStartSequence <= 0)
            {
                // Show Image
                ToggleCanvas(true);
                StartTimer(true, "");
            }

            // Handle Spacebar Released (Deactivation)
            if (Input.GetKeyUp(KeyCode.Space) && isTiming)
            {
                // Hide Image
                StartTimer(false, "");
                ToggleCanvas(false);

                // Reset Focus to center and change image if study started
                if (!resettingFocus)
                {
                    resetFocusDistanceToCenter(1.5f);

                }
            }

            // Choose Focus Mode
            if (FocusMode == 1) { FocusConventionalHead(); }
            else if (FocusMode == 2) { FocusEyeGazeContingent(); }

        }
        // Always do this
        AlignCanvasWithCamera();
    }

    void AlignCanvasWithCamera()
    {
        canvas.transform.position = referencedCamera.transform.position + referencedCamera.transform.forward * zOffsetCanvasToCamera;
        canvas.transform.rotation = referencedCamera.transform.rotation;
    }

    void FocusConventionalHead()
    {
        // Get the pitch angle (rotation around the X-axis) from the camera — this reflects up/down head tilt
        float cameraPitch = referencedCamera.transform.eulerAngles.x;

        // Convert pitch from 0–360° to -180° to +180° to handle angle wrapping correctly
        if (cameraPitch > 180f) cameraPitch -= 360f;

        // Clamp pitch to the desired input range (from 20 degrees down to -5 degrees up to horizontal )
        float clampedPitch = Mathf.Clamp(cameraPitch, -5f, 20f);

        // Define input pitch range and corresponding override output range
        float minPitch = -5f;  // max upward tilt
        float maxPitch = 20f;   // max downard tilt
        float minOverride = 3650f; // distance at -5° up
        float maxOverride = 4900f; // distance at 20° down

        // Normalize pitch (0 at -30°, 1 at 0°)
        float t = (clampedPitch - minPitch) / (maxPitch - minPitch);

        // Interpolate between min and max override based on inverted pitch t
        distanceToPlane = (int)Mathf.Lerp(minOverride, maxOverride, t);

        // Apply the calculated override to the depth of field setting
        BeautifySettings.settings.depthOfFieldDistance.value = distanceToPlane;
        BeautifySettings.settings.depthOfFieldDistance.overrideState = true;
    }

    void FocusEyeGazeContingent()
    {
        if (eyeGaze == null || headTransform == null || cursor == null)
        {
            Debug.LogError("Missing references in EyeGazeCursorWithHeadAlignment script!");
            return;
        }

        if (OVRPlugin.eyeTrackingEnabled && eyeGaze.gameObject.activeInHierarchy)
        {
            // Get the raw gaze direction and apply offset correction
            Vector3 gazeDirection = eyeGaze.transform.forward;

            // Adjust gaze direction based on head rotation
            gazeDirection = headTransform.rotation * gazeDirection;

            // Project gaze onto a plane in front of the camera (same as before)
            Vector3 targetPosition = headTransform.position + (gazeDirection * (zOffsetCanvasToCamera));

            // Convert to local space relative to the head
            Vector3 localTargetPosition = headTransform.InverseTransformPoint(targetPosition);

            // Apply offset correction to counteract drift
            localTargetPosition.x = (localTargetPosition.x + gazeXOffset) * scaleX;
            localTargetPosition.y = (localTargetPosition.y + gazeYOffset) * scaleY;

            // Maintain depth consistency
            localTargetPosition.z = 2300;

            // Convert back to world space
            Vector3 fixedWorldPosition = headTransform.TransformPoint(localTargetPosition);

            // Smooth cursor movement
            cursor.transform.position = Vector3.Lerp(cursor.transform.position, fixedWorldPosition, Time.deltaTime * smoothing);
        }

        // --- NEW: Raycast to the "plane" object ---
        Ray ray = new Ray(headTransform.position, (cursor.transform.position - headTransform.position).normalized);

        if (Physics.Raycast(ray, out RaycastHit hit, 10000f)) // 100f = max raycast distance
        {
            if (hit.collider.gameObject.name == "FocusPlane") // Check if the object is the one you want
            {
                // 1. Define the reference plane in front of the camera (same logic as before)
                Vector3 cameraPosition = referencedCamera.transform.position;
                Vector3 cameraForward = referencedCamera.transform.forward;

                // The reference plane will be at a fixed distance in front of the camera (e.g., 5 units away)
                float referenceDistance = 5f;  // Adjust this distance if needed
                Vector3 referencePlanePosition = cameraPosition + cameraForward * referenceDistance;

                // 2. Calculate the vector from the reference plane to the cursor hit point
                Vector3 hitPoint = hit.point;
                Vector3 vectorToReferencePlane = hitPoint - referencePlanePosition;

                // 3. Project the vector onto the camera's forward direction to get the minimal distance
                distanceToPlane = (int)Vector3.Dot(vectorToReferencePlane, cameraForward);

                // Debug: Output distance and cursor position for verification
                //Debug.Log($"Cursor moved to: {hit.point}, focus set. Minimal distance to reference plane: {distanceToPlane}");

                // 4. Update the depth of field distance for visual effect
                BeautifySettings.settings.depthOfFieldDistance.value = distanceToPlane;
                BeautifySettings.settings.depthOfFieldDistance.overrideState = true;
            }
        }
    }

    void ToggleCanvas(bool isActive)
    {
        int uiLayer = LayerMask.NameToLayer("UIVisible"); // Get the layer index

        if (uiLayer == -1)
        {
            Debug.LogError("Layer 'UIVisible' not found. Make sure it exists in the project settings.");
            return;
        }

        // Find all Focus Relevant Game Objects and Enable/Disable them
        GameObject[] uiElements = FindObjectsOfType<GameObject>(true); // Find ALL objects, even inactive ones

        foreach (GameObject obj in uiElements)
        {
            if (obj.layer == uiLayer) // Check if object is on the "UIVisible" layer
            {
                if (isActive)
                {
                    Transform parent = obj.transform.parent;
                    while (parent != null)
                    {
                        parent.gameObject.SetActive(true); // Ensure parent is active
                        parent = parent.parent; // Move up the hierarchy
                    }
                }

                obj.SetActive(isActive); // Finally, activate/deactivate the object itself
                BeautifySettings.settings.depthOfField.Override(isActive); // Enable Depth Of Field

            }
        }
        // Toggle Cursor and Canvas opposite to the Focus Content
        cursor.SetActive(!isActive);
        centerMark.SetActive(!isActive);

    }

    void StartTimer(bool status, string buttonName)
    {
        // Timer to measure interaction times
        if (status)
        {
            startTime = Time.time; // Store the start time
            isTiming = true;
        }
        else
        {
            if (isTiming && studyStarted)
            {
                float elapsedTime = Time.time - startTime; // Calculate elapsed time

                // Ignore short button presses below 250ms, as those are not real focus methods
                if (elapsedTime > 0.25f)
                {
                    // Write to CSV file
                    focusError = calculateFocusError();
                    SaveCSV(targetImg, elapsedTime, focusError, FocusMode);
                    ChangeFocusImage();
                }
                isTiming = false;

            }
            else
            {
                // Only though error when study sequence is not currently starting
                if (studyStartSequence == -1) Debug.LogError("Timer was not running.");
            }
        }
    }

    void SaveCSV(int targetImg, float duration, int focusError, int FocusMode)
    {
        if (!studyStarted) return;
        // Get the current timestamp
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // Create the data line with timestamp
        string dataLine = $"{FocusMode};{timestamp};{targetImg.ToString(CultureInfo.InvariantCulture)};{duration.ToString(CultureInfo.InvariantCulture)};{focusError.ToString(CultureInfo.InvariantCulture)}\n";

        try
        {
            File.AppendAllText(filePath, dataLine);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error saving tracking data: " + e.Message);
        }
    }

    void SaveCSVHeader(string msg)
    {
        try
        {
            // Get the current timestamp
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Append the message with the timestamp
            string messageWithTimestamp = $"{msg};{timestamp}\n";

            // Save the message with the timestamp to the file
            File.AppendAllText(filePath, messageWithTimestamp);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error saving tracking data: " + e.Message);
        }
    }

    void CalibrateEyeGaze()
    {
        isCalibrating = true;
        cursor.SetActive(false);
        // Get all children of the specified parent object
        children = new Transform[CalibrationParent.transform.childCount];
        for (int i = 0; i < CalibrationParent.transform.childCount; i++)
        {
            children[i] = CalibrationParent.transform.GetChild(i);
            children[i].gameObject.SetActive(false); // Deactivate all initially
        }
        ActivateNextCalibrationPoint();
    }

    void ActivateNextCalibrationPoint()
    {
        if (calibrationIndex > 0 && calibrationIndex <= children.Length)
        {
            children[calibrationIndex - 1].gameObject.SetActive(false); // Deactivate previous child
        }
        if (calibrationIndex < children.Length)
        {
            children[calibrationIndex].gameObject.SetActive(true);

            // Add child position to coordinate sum
            Vector3 position = children[calibrationIndex].localPosition;
            targetPoints[calibrationIndex] = position;
        }
        // When all points are recorded
        else
        {
            ComputeGazeCalibration();
        }
    }

    void RecordCalibrationPoint()
    {
        if (isCalibrating && calibrationIndex < rawGazeData.Length)
        {

            // Get the raw gaze direction and apply offset correction
            Vector3 gazeDirection = eyeGaze.transform.forward;

            // Adjust gaze direction based on head rotation
            gazeDirection = headTransform.rotation * gazeDirection;

            // Project gaze onto a plane in front of the camera
            Vector3 targetPosition = headTransform.position + (gazeDirection * (zOffsetCanvasToCamera));

            // Convert to local space relative to the head
            Vector3 localTargetPosition = headTransform.InverseTransformPoint(targetPosition);

            rawGazeData[calibrationIndex] = localTargetPosition;
            calibrationIndex++;
            ActivateNextCalibrationPoint();
        }
    }

    void ComputeGazeCalibration()
    {
        int count = rawGazeData.Length;

        // Compute average offset
        float sumOffsetX = 0f, sumOffsetY = 0f;
        for (int i = 0; i < count; i++)
        {
            sumOffsetX += targetPoints[i].x - rawGazeData[i].x;
            sumOffsetY += targetPoints[i].y - rawGazeData[i].y;
        }
        gazeXOffset = sumOffsetX / count;
        gazeYOffset = sumOffsetY / count;

        // Compute scale factors using points 3 and 4 (horizontal & vertical difference)
        float scaleX1 = (targetPoints[3].x - targetPoints[4].x) /
                        ((rawGazeData[3].x + gazeXOffset) - (rawGazeData[4].x + gazeXOffset));

        float scaleY1 = (targetPoints[3].y - targetPoints[4].y) /
                        ((rawGazeData[3].y + gazeYOffset) - (rawGazeData[4].y + gazeYOffset));

        // Compute scale factors using points 5 and 6 (horizontal & vertical difference)
        float scaleX2 = (targetPoints[5].x - targetPoints[6].x) /
                        ((rawGazeData[5].x + gazeXOffset) - (rawGazeData[6].x + gazeXOffset));

        float scaleY2 = (targetPoints[5].y - targetPoints[6].y) /
                        ((rawGazeData[5].y + gazeYOffset) - (rawGazeData[6].y + gazeYOffset));

        // Average the scale values for a more robust calibration
        scaleX = (scaleX1 + scaleX2) / 2f;
        scaleY = (scaleY1 + scaleY2) / 2f;

        // Post Calibration Routine
        isCalibrating = false;
        placeCenterMark();
    }

    void ChangeFocusImage()
    {
        currentIndex++;
        // Check if all materials used
        if (currentIndex >= materialPool.Count)
        {
            SaveCSVHeader("StudyEnded");
            Debug.LogError("Study Ended, all 20 Images focused");
            return;
        }

        // Apply next material
        if (targetObject != null)
        {
            Renderer rend = targetObject.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = materialPool[currentIndex];
            }
        }


    }

    void LoadShuffledMaterials()
    {

        materialPool = new List<Material>();

        // Find all materials and load them to materialPool
        for (int i = 1; i <= 20; i++)
        {
            Material mat = Resources.Load<Material>($"FocusImages/{i}");
            if (mat != null)
            {
                materialPool.Add(mat);
            }
            else
            {
                Debug.LogError($"Focus Image {i} not found in Resources/Materials/");
            }
        }

        // Shuffle using Fisher-Yates
        for (int i = 0; i < materialPool.Count; i++)
        {
            int rand = UnityEngine.Random.Range(i, materialPool.Count);
            var temp = materialPool[i];
            materialPool[i] = materialPool[rand];
            materialPool[rand] = temp;
        }
    }

    void studyStartMethod()
    {
        // Method that runs through toggling the right items, while indicating the sounds to the user
        // Increase the timer
        if (!studyStarted) studyStartTimer += Time.deltaTime;  // Increment the timer


        // Start Study
        if (studyStartSequence == 0 && studyStartTimer >= studyStartTime)
        {
            audioSource.PlayOneShot(studyStartSound);
            studyStartSequence++;
            ToggleCanvas(false);
            studyStartSequence = 1;

        }

        else if (studyStartSequence == 1 && studyStartTimer >= studyStartTime + 2)
        {
            audioSource.PlayOneShot(studyStartSound);
            studyStartSequence++;
            ToggleCanvas(false);
            resetFocusDistanceToCenter(1f);

            studyStartSequence = 2;
        }

        else if (studyStartSequence == 2 && studyStartTimer >= studyStartTime + 3.5f)
        {
            audioSource.PlayOneShot(studyStartSound);
            // Initialize filePath
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fileName = $"{StudyID}_focus_log.csv"; // adds the StudyID into the filename
            filePath = Path.Combine(desktopPath, fileName);
            SaveCSVHeader("Focus Study Started");  // Save the header once the study starts
            SaveCSVHeader("focusmode;timestamp;targetImg;duration;error;");  // Save the header once the study starts
            Debug.Log("Study started");
            studyStarted = true;
            studyStartSequence = -1;
            ChangeFocusImage();
            centerMark.SetActive(true);
            cursor.SetActive(true);

        }
    }

    public int calculateFocusError()
    {

        float centerCoordinate = 2000f;
        float centerDistance = 4300f;

        // Get the material corresponding to the current index
        Material currentMat = materialPool[currentIndex];

        // Parse the material name ("1" to "20") to int
        targetImg = int.Parse(currentMat.name);

        // Use that number to get the right coordinates
        ImageCoord currentCoord = FocusTarget[targetImg];
        float x = currentCoord.x;
        float y = currentCoord.y;

        // Calculate focus distance using float math
        float distanceToCenter = (-(x - centerCoordinate) * tan + (y - centerCoordinate) * tan);
        focusDistance = distanceToCenter + centerDistance;

        // Calculate focus error
        float focusError = distanceToPlane - focusDistance;

        // Optionally round only the final result if needed
        return Mathf.RoundToInt(focusError);
    }

    public void LoadFocusTargets()
    {
        // Coordinates where the upper triangle of each image is located
        FocusTarget[1] = new ImageCoord(198, 1233);
        FocusTarget[2] = new ImageCoord(396, 1854);
        FocusTarget[3] = new ImageCoord(614, 651);
        FocusTarget[4] = new ImageCoord(819, 1655);
        FocusTarget[5] = new ImageCoord(1028, 434);
        FocusTarget[6] = new ImageCoord(1034, 2457);
        FocusTarget[7] = new ImageCoord(1236, 2887);
        FocusTarget[8] = new ImageCoord(1243, 824);
        FocusTarget[9] = new ImageCoord(1444, 447);
        FocusTarget[10] = new ImageCoord(1448, 2267);
        FocusTarget[11] = new ImageCoord(1650, 1657);
        FocusTarget[12] = new ImageCoord(1663, 2677);
        FocusTarget[13] = new ImageCoord(1868, 2067);
        FocusTarget[14] = new ImageCoord(1871, 242);
        FocusTarget[15] = new ImageCoord(2070, 615);
        FocusTarget[16] = new ImageCoord(2079, 2250);
        FocusTarget[17] = new ImageCoord(2285, 2683);
        FocusTarget[18] = new ImageCoord(2496, 1651);
        FocusTarget[19] = new ImageCoord(2704, 831);
        FocusTarget[20] = new ImageCoord(2913, 1644);
    }

    public void placeCenterMark()
    {
        // Places the center mark in front of the user
        cursor.SetActive(true);
        centerMark.SetActive(true);
        Vector3 positionInFront = referencedCamera.transform.position + referencedCamera.transform.forward * 2500;
        positionInFront.y = -350f; // Force Y to be -350 to achieve downward tilt of head
        centerMark.transform.position = positionInFront;
    }

    public void resetFocusDistanceToCenter(float time)
    {
        // Helps to reset the focus distance to always be in the center where the cursor starts
        resettingFocus = true;
        float distanceToPlane = 4300f;

        BeautifySettings.settings.depthOfField.Override(true); // Enable Depth Of Field
        BeautifySettings.settings.depthOfFieldDistance.value = distanceToPlane;
        BeautifySettings.settings.depthOfFieldDistance.overrideState = true;

        // Start timer to turn off DoF after 500ms
        StartCoroutine(DisableDepthOfFieldAfterDelay(time));
    }

    IEnumerator DisableDepthOfFieldAfterDelay(float delay)
    {
        // Required to reset Focus Distance to Center
        yield return new WaitForSeconds(delay);
        BeautifySettings.settings.depthOfField.Override(false); // Disable Depth Of Field
        resettingFocus = false;
    }
}
