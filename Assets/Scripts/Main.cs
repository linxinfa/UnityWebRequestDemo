using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Net;

public class Main : MonoBehaviour
{
    public Slider progressSlider;
    public Text progressLbl;

    public Button uploadBtn;
    public Button downloadBtn;
    public Button stopDownloadBtn;

    private const string UPLOAD_URL = "http://localhost:8988";
    private const string UPLOAD_FILE_NAME = "upload_test.png";
    private const string DOWNLOAD_FILE_NAME = "download_test.png";


    private IEnumerator m_downloadCoroutine = null;
    private Stream m_serverResponseStream = null;
    private Stream m_localFileStream = null;
    /// <summary>
    /// 下载读文件缓存，一帧读64个字节，这样可以很慢地写入本地文件，方便测试断点续传
    /// </summary>
    private byte[] m_downloadBuffer = new byte[128];

    void Start()
    {
        // 上传文件
        uploadBtn.onClick.AddListener(() =>
        {
            var filePath = Path.Combine(Application.dataPath, UPLOAD_FILE_NAME);
            var fileName = Path.GetFileName(filePath);
            byte[] data = ReadLocalFile(filePath);
            Debug.Log(data.Length);
            StartCoroutine(UploadFile(UPLOAD_URL, fileName, data));
        });

        // 下载文件
        downloadBtn.onClick.AddListener(() =>
        {
            StopDownload();
            m_downloadCoroutine = DownloadFile(UPLOAD_URL, DOWNLOAD_FILE_NAME);
            StartCoroutine(m_downloadCoroutine);
        });

        // 停止下载
        stopDownloadBtn.onClick.AddListener(() => 
        {
            StopDownload();
        });
    }

    private void StopDownload()
    {
        if (null != m_downloadCoroutine)
        {
            StopCoroutine(m_downloadCoroutine);
            m_downloadCoroutine = null;
        }
        if(null != m_serverResponseStream)
        {
            m_serverResponseStream.Close();
            m_serverResponseStream.Dispose();
            m_serverResponseStream = null;

            m_localFileStream.Close();
            m_localFileStream.Dispose();
            m_localFileStream = null;
        }
    }

    /// <summary>
    /// 读取文件的字节流
    /// </summary>
    /// <returns></returns>
    private byte[] ReadLocalFile(string filePath)
    {
        byte[] data = null;

        using (FileStream fs = File.OpenRead(filePath))
        {
            int index = 0;
            long len = fs.Length;
            data = new byte[len];
            // 考虑文件可能很大，进行分段读取
            int offset = data.Length > 1024 ? 1024 : data.Length;
            while (index < len)
            {
                int readByteCnt = fs.Read(data, index, offset);
                index += readByteCnt;
                long leftByteCnt = len - index;
                offset = leftByteCnt > offset ? offset : (int)leftByteCnt;
            }
            Debug.Log("Read Done");
        }
        return data;
    }

    /// <summary>
    /// 通过http post上传文件
    /// </summary>
    /// <param name="url">http地址</param>
    /// <param name="data">要上传的文件的字节流</param>
    /// <returns></returns>
    IEnumerator UploadFile(string url, string fileName, byte[] data)
    {
        WWWForm form = new WWWForm();
        form.AddField("desc", "test upload file");
        form.AddBinaryData("file_data", data, fileName, "application/x-gzip");
        // 使用UnityWebRequest
        UnityWebRequest request = UnityWebRequest.Post(url, form);
        var result = request.SendWebRequest();
        if (request.isNetworkError)
        {
            Debug.LogError(request.error);
        }
        while (!result.isDone)
        {
            yield return null;
            // 更新上传日志进度条
            progressSlider.value = request.uploadProgress;
            progressLbl.text = Math.Floor(request.uploadProgress * 100) + "%";
        }

        Debug.Log("finish upload, http return msg: \n" + request.downloadHandler.text);
    }

    /// <summary>
    /// 下载文件
    /// </summary>
    /// <param name="url">http地址</param>
    /// <param name="fileName">要下载的文件名</param>
    /// <returns></returns>
    IEnumerator DownloadFile(string url, string fileName)
    {
        Debug.Log("DownloadFile");
        //Head方法可以获取到文件的全部长度
        UnityWebRequest huwr = UnityWebRequest.Head(url + "/" + fileName);
        yield return huwr.SendWebRequest();
        if (huwr.isNetworkError || huwr.isHttpError)
        {
            //如果出错, 输出 错误信息
            Debug.Log(huwr.error);
            yield break;
        }
        
        //首先拿到文件的全部长度
        long totalLength = long.Parse(huwr.GetResponseHeader("Content-Length"));
        Debug.Log("totalLength: " + totalLength);
#if UNITY_EDITOR
        var filePath = Application.streamingAssetsPath + "/" + fileName;
#else
        // 真机上保存到persistentDataPath路径中
        var filePath = Application.persistentDataPath + "/" + fileName;
#endif
        using (m_localFileStream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write))
        {
            long nowFileLength = m_localFileStream.Length; //当前文件长度
            Debug.Log(m_localFileStream.Length);
            if (nowFileLength < totalLength)
            {
                //从头开始索引，长度为当前文件长度
                m_localFileStream.Seek(nowFileLength, SeekOrigin.Begin);
                Debug.Log("seek : " + nowFileLength);
            }
            else
            {
                Debug.Log("dont need download");
                yield break;
            }

            HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(url + "/" + fileName);
            request.AddRange(nowFileLength);
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            m_serverResponseStream = response.GetResponseStream();
            Debug.Log("responseStream.Length: " + m_serverResponseStream.Length);
            int readSize = 0;
            while (true)
            {
                readSize = m_serverResponseStream.Read(m_downloadBuffer, 0, m_downloadBuffer.Length);
                if (readSize > 0)
                {
                    // 写入文件 
                    m_localFileStream.Write(m_downloadBuffer, 0, readSize);
                    nowFileLength += readSize;
                    //展示下载进度
                    progressSlider.value = (float)nowFileLength / totalLength;
                    progressLbl.text = Math.Floor((float)nowFileLength / totalLength * 100) + "%";
                }
                else
                {
                    // 下载完成了
                    progressSlider.value = 1;
                    progressLbl.text = 100 + "%";
                    m_serverResponseStream.Close();
                    m_serverResponseStream.Dispose();
                    break;
                }
                yield return null;
            }
            Debug.Log("download Done");

        }
    }
}
