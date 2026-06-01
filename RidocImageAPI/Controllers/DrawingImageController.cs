using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using jp.co.ricoh.ridoc.smartnavi.model;
using jp.co.ricoh.ridoc.smartnavi;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace RidocImageAPI.Controllers
{
    [ApiController]
    [Route("v1/[controller]")]
    public class DrawingImageController : ControllerBase
    {
        private readonly ILogger<DrawingImageController> _logger;
        private readonly IWebHostEnvironment _environment;

        public DrawingImageController(ILogger<DrawingImageController> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        [HttpGet(Name = "GetDrawingImage")]
        public async Task<IActionResult> GetAsync([FromQuery] string docId, [FromQuery] string imgType)
        {
            _logger.LogInformation("Received request with docId: {0}, imgType: {1}", docId, imgType);

            if (string.IsNullOrEmpty(docId))
            {
                _logger.LogWarning("Missing parameter: docId");
                return BadRequest("DocumentId is required.");
            }

            if (string.IsNullOrEmpty(imgType))
            {
                _logger.LogWarning("Missing parameter: imgType");
                return BadRequest("ImageType is required.");
            }

            string documentId = "", documentTypeId = "";

            try
            {
                // 🔹 RSN 接続
                RsnSystem rsnSystem = new RsnSystem();
                string serverURL = "http://192.168.1.5:8080/rsn/";
                string user = "imotoseiki", password = "0750";

                _logger.LogInformation("Connecting to RSN System: {0}", serverURL);
                RsnCabinet rsnCabinet = await Task.Run(() => rsnSystem.Connect(serverURL, user, password));

                // 🔹 検索条件の作成
                RsnSearchCondition rsnSearchCondition = new RsnSearchCondition
                {
                    documentTypeId = "f94711dd-b737-49ba-b68a-bc5cef424019",
                    // documentTypeId = "efc2edb5-7235-4ba2-9779-ef59bd67744a",
                    searchDocument = true,
                    searchFolder = true,
                    searchSubFolder = true,
                    rangeFolderId = null,
                    keywords = new List<string> { docId }
                };

                // 🔹 ドキュメント検索
                _logger.LogInformation("Searching for document with keyword: {0}", docId);
                RsnSearchResultSet searchResult = await Task.Run(() => rsnSystem.Search(rsnSearchCondition));
                long documentCount = searchResult.GetDocumentCount();

                if (documentCount == 0)
                {
                    _logger.LogWarning("No documents found for keyword: {0}", docId);
                    return NotFound("No documents found.");
                }

                // 🔹 検索結果を取得
                List<RsnDocument> documentList = searchResult.GetDocumentList(0, Convert.ToInt32(documentCount));

                // 🔹 検索結果をログに出力
                _logger.LogInformation("Found {0} documents:", documentCount);

                foreach (var doc in documentList)
                {
                    documentId = doc.documentProperty.id.ToString();
                    documentTypeId = doc.documentProperty.documentTypeId.ToString();

                    // 🔹 各ドキュメントのプロパティをログ出力
                    _logger.LogInformation("Document ID: {0}", documentId);
                    _logger.LogInformation("Document Type ID: {0}", documentTypeId);
                    _logger.LogInformation("Document Name: {0}", doc.documentProperty.name);
                    _logger.LogInformation("Size: {0}", doc.documentProperty.size);
                }

                //searchResult.Dispose();

                if (string.IsNullOrEmpty(documentId))
                    return NotFound("No documents found for the given keyword.");

                _logger.LogInformation("Using document ID: {0}", documentId);

                // 🔹 ドキュメントを取得
                RsnDocument document = await Task.Run(() => rsnSystem.GetDocument(documentId));

                // 🔹 ファイル名を決定
                string fileName = imgType == "TN" ? $"{document.name}_tn.jpg" : $"{document.name}.jpg";
                string filePath = Path.Combine(_environment.ContentRootPath, "Images", "Drawings", fileName);

                // 🔹 画像を作成
                CreateImageFile(document, imgType, filePath);

                // 🔹 画像作成待機
                await WaitForFileCreation(filePath);

                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogError("Image file not found: {0}", filePath);
                    return NotFound("Image file not found.");
                }

                _logger.LogInformation("Serving image file: {0}", filePath);

                var memoryStream = new MemoryStream();
                using (var image = await Image.LoadAsync(filePath))
                {
                    image.Save(memoryStream, new JpegEncoder());
                }

                memoryStream.Seek(0, SeekOrigin.Begin);
                return File(memoryStream, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request.");
                return StatusCode(500, new { message = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        /// <summary>
        /// 🔹 画像を作成するメソッド
        /// </summary>
        private void CreateImageFile(RsnDocument document, string imgType, string filePath)
        {
            try
            {
                _logger.LogInformation("Creating image file: {0}, imgType: {1}", filePath, imgType);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                Stream stream = fileStream;
                RsnSection section;

                if (imgType == "TN")
                {
                    section = document.ReadSectionData(1, RsnDocument.OPTION_THUMBNAIL, ref stream);
                }
                else if (imgType == "ORG")
                {
                    section = document.ReadSectionData(1, RsnDocument.OPTION_FILE_DATA, ref stream);
                }
                else
                {
                    _logger.LogError("Invalid imgType: {0}", imgType);
                    throw new ArgumentException("Invalid image type.");
                }

                if (section == null)
                {
                    throw new Exception("Failed to read section data.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating image file: {0}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 🔹 ファイルが作成されるまで待つ
        /// </summary>
        private async Task WaitForFileCreation(string filePath)
        {
            for (int i = 0; i < 10; i++)
            {
                if (System.IO.File.Exists(filePath)) return;
                await Task.Delay(500);
                _logger.LogWarning("Waiting for file creation... {0}", filePath);
            }

            _logger.LogError("File creation timed out: {0}", filePath);
            throw new Exception("File creation timed out.");
        }
    }
}
