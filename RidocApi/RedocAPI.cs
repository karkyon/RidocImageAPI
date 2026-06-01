namespace RidocAPI
{
    public class RidocAPI
    {
        public string? DocumentId { get; set; }
        public string? ImageType { get; set; }
    }
    public interface IRidocAPIContainer
    {
        System.IO.FileStream GetImage();
    }
}
