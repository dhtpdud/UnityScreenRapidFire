using System.Collections;
using System.Diagnostics;
using UnityEngine;
//using OpenCVForUnity.CoreModule;
using System.Collections.Generic;
using Application = UnityEngine.Application;
using Directory = System.IO.Directory;
using File = System.IO.File;
using System;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using UnityEngine.Scripting; // ������ �÷��� ���̴ϱ� ������ ����
using UnityEngine.Profiling;
using System.Threading;

public class ScreenRecorder : MonoBehaviour
{
    [Header("Options")]
    [SerializeField]
    private int recordingSec = 30;
    public string RecordingSec
    {
        set
        {
            recordingSec = int.Parse(value);
        }
    }
    [SerializeField]
    private int captureFrameRate = 30;
    public string CaptureFrameRate
    {
        set
        {
            captureFrameRate = int.Parse(value);
        }
    }
    [SerializeField]
    private float gameSpeed = 30;
    public string GameSpeed
    {
        set
        {
            gameSpeed = float.Parse(value);
        }
    }
    public bool VSyncEnable
    {
        set
        {
            QualitySettings.vSyncCount = value ? originVSyncCount : 0;
        }
    }
    [SerializeField]
    private string directoryPath = "FrameCaptures";
    public string DirectoryPath
    {
        get => directoryPath;
        set
        {
            directoryPath = value;
        }
    }


    [Header("UI")]
    [SerializeField]
    Canvas canvas;
    [SerializeField]
    private Text textTimer;
    [SerializeField]
    private Text textCounter;
    [SerializeField]
    private Text textFPS;

    private int counterIndex = 1;

    private Camera mainCam = null;
    private int ScreenWidth = Screen.width;
    private int ScreenHeight = Screen.height;
    private Rect screenRect;


    //ĳ�̿� ���� ����
    private int originVSyncCount;
    private int originTargetFrameRate;

    private float deltatimeCache;
    private float lastFPS;

    private IEnumerator updateRoutine;
    private IEnumerator recordingRoutine;
    private IEnumerator encodingRoutine;
    private YieldInstruction yieldCacheWaitUpdate = new WaitForEndOfFrame();

    private Stopwatch bechmarkWatch = new Stopwatch();

    private void Awake()
    {
        originVSyncCount = QualitySettings.vSyncCount;
        mainCam = Camera.main;
        canvas.worldCamera = mainCam;
    }
    private void Update()
    {
        deltatimeCache = Time.deltaTime;
        textFPS.text = string.Format("{0:N1} FPS", lastFPS = 1.0f / deltatimeCache);
    }
    IEnumerator RecordingUpdateRoutine()
    {
        float recordingTime = 0;
        counterIndex = 1;
        while (true)
        {
            textTimer.text = $"{recordingTime += deltatimeCache}��";
            textCounter.text = counterIndex++.ToString();
            yield return yieldCacheWaitUpdate;
        }
    }

    //Record ��ư Ŭ���� ȣ��
    public void ToggleRecord()
    {
        if (recordingRoutine != null)
        {
            Debug.Log("��ȭ �ߴ�");
            StopCoroutine(recordingRoutine);
            recordingRoutine = null;
            return;
        }
        if (encodingRoutine != null)
        {
            Debug.Log("���ڵ� �ߴ�");
            StopCoroutine(encodingRoutine);
            encodingRoutine = null;
            return;
        }
        directoryPath = $"{directoryPath}\\{recordingSec}s_{captureFrameRate}fps_{gameSpeed}x";

        if (string.IsNullOrEmpty(directoryPath))
        {
            Debug.Log("�߸��� ���");
            return;
        }

        if (!Directory.Exists(directoryPath))
        {
            Debug.Log("���� ����");
            Directory.CreateDirectory(directoryPath);
        }

        screenRect = new Rect(0, 0, ScreenWidth, ScreenHeight);

        //��ȭ ����
        StartCoroutine(recordingRoutine = RecordingRoutine());
    }

    IEnumerator RecordingRoutine()
    {
        //�ʱⰪ ����
        originTargetFrameRate = Application.targetFrameRate;
        Application.targetFrameRate = (int)(captureFrameRate * gameSpeed);
        QualitySettings.vSyncCount = 0;
        Time.timeScale = 0;

        //�������� Canvas�� RenderTexture�� ���� ĸ�ĵ��� ����
        //���� Canvas renderMode��带 ScreenSpaceCamera�� ����
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.planeDistance = 0.4f;

        bechmarkWatch.Reset();
        bechmarkWatch.Start();

        List<RenderTexture> capturedFrames = new List<RenderTexture>();

        //targetFrameRate ���濡 ���� fps �Ҿ��� �ð�
        yield return new WaitForSecondsRealtime(0.1f);

        StartCoroutine(updateRoutine = RecordingUpdateRoutine());

        //GC ������ũ ����
#if !UNITY_EDITOR
        GarbageCollector.GCMode = GarbageCollector.Mode.Manual;
#endif

        Time.timeScale = gameSpeed;

        //������ ���� Vram �Ѱ輱�� 70%��...(���Ѵٸ� ��������)
        long limitVRamSize = (long)(SystemInfo.graphicsMemorySize * 0.7f);

        //������ ĳ��
        for (int i = 0; i < recordingSec * captureFrameRate; i++)
        {
            mainCam.targetTexture = new RenderTexture(ScreenWidth, ScreenHeight, 16);
            capturedFrames.Add(mainCam.targetTexture);

            //VRam ����
            //VRam�� 70%�̻� �������� ���ڵ� ����
            if (Profiler.GetAllocatedMemoryForGraphicsDriver() / 1000000 >= limitVRamSize)
            {
                Debug.Log("�޸� ������, �߰� ���ڵ� ����");
                yield return encodingRoutine = EncodingRoutine(capturedFrames);
                Debug.Log("�߰� ���ڵ� �Ϸ�");
            }

            yield return yieldCacheWaitUpdate;
        }
        bechmarkWatch.Stop();
        Debug.Log($"�Կ� �ð�: {bechmarkWatch.ElapsedMilliseconds / 1000f}��");

        StopCoroutine(updateRoutine);
        updateRoutine = null;
        recordingRoutine = null;

        //ĳ�̵� ������ ���ڵ� + ���� ���� ����
        yield return encodingRoutine = EncodingRoutine(capturedFrames);
    }

    IEnumerator EncodingRoutine(List<RenderTexture> capturedFrame)
    {
#if !UNITY_EDITOR
        GarbageCollector.GCMode = GarbageCollector.Mode.Enabled;
#endif

        Time.timeScale = 0;
        mainCam.targetTexture = null;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        GC.Collect();
        Resources.UnloadUnusedAssets();

        bechmarkWatch.Reset();
        bechmarkWatch.Start();

        //���ڵ� ����
        //GPU�� �Ѱ踦 �ִ��� ���� �ø� �� �ֵ��� ī�޶� OFF
        mainCam.enabled = false;
        for (int i = 0; i < capturedFrame.Count; i++)
        {
            EncodeImage(i, capturedFrame[i], directoryPath);
            yield return yieldCacheWaitUpdate;
        }
        bechmarkWatch.Stop();
        Debug.Log($"���ڵ� �ð�: {bechmarkWatch.ElapsedMilliseconds / 1000f}��");


        //���ڵ� �Ϸ�
        //�޸� ����, ���ð� �ʱ�ȭ
        mainCam.enabled = true;

        Time.timeScale = 1;
        Application.targetFrameRate = originTargetFrameRate;
        QualitySettings.vSyncCount = originVSyncCount;

        capturedFrame.Clear();
        Resources.UnloadUnusedAssets();
        encodingRoutine = null;
    }

    void EncodeImage(int currentCount, RenderTexture frame, string directoryPath)
    {
        Texture2D convertedFrame = ConvertToTexture2D(frame);

        // Texture PNG bytes�� ���ڵ�.
        // GPU ���񱸰�
        byte[] texturePNGBytes = convertedFrame.EncodeToPNG();
        string filePath = $"{directoryPath}\\{currentCount + 1}.png";

        //OpenCV�׽�Ʈ
        /*OpenCVForUnity.UnityUtils.Utils.texture2DToMat(texture2D, mat);
        OpenCVForUnity.ImgcodecsModule.Imgcodecs.imwrite(filePath, mat);*/


        //��Ƽ������ �̻�� ��� 3.5�� �� ��Ƽ������ ��� ��� 3��, �� 14.2% ���� ���
        //(i7 - 13���� ����)
        Thread thread = new Thread(new ThreadStart(() => File.WriteAllBytes(filePath, texturePNGBytes)));
        thread.Start();
    }

    Texture2D ConvertToTexture2D(RenderTexture rTex)
    {
        // TextureFormat���� RGB24 �� ���İ� �������� �ʴ´�.
        Texture2D tex = new Texture2D(ScreenWidth, ScreenHeight, TextureFormat.RGB24, true);
        RenderTexture.active = rTex;

        // GPU ���񱸰�
        tex.ReadPixels(screenRect, 0, 0);
        tex.Apply();
        return tex;
    }
}
