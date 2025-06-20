namespace DEMBuilder.Models
{
    public class GpsPoint
    {
        public int ReceiverId { get; }
        public double Latitude { get; }
        public double Longitude { get; }
        public double Altitude { get; }
        public int FixQuality { get; }
        public int NumberOfSatellites { get; }
        public double Hdop { get; } // Horizontal Dilution of Precision

        public GpsPoint(int receiverId, double latitude, double longitude, double altitude, int fixQuality, int numberOfSatellites, double hdop)
        {
            ReceiverId = receiverId;
            Latitude = latitude;
            Longitude = longitude;
            Altitude = altitude;
            FixQuality = fixQuality;
            NumberOfSatellites = numberOfSatellites;
            Hdop = hdop;
        }
    }
}
