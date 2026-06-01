using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace RidocAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RidocImageController : ControllerBase
    {
        public RidocImageController()
        {

        }

        [HttpGet()]
        public ActionResult Get([FromQuery] string text)
        {
            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrCodeData);
            var bytes = qrCode.GetGraphic(10);
            return File(bytes, "image/png", "example.png");
        }
    }
}
