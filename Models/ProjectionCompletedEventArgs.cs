using System;
using System.Collections.Generic;
using DEMBuilder.Models;

namespace DEMBuilder.Models
{
    public class ProjectionCompletedEventArgs : EventArgs
    {
        public List<GpsPoint> ProjectedPoints { get; }
        public double ReferenceLatitude { get; }
        public double ReferenceLongitude { get; }

        public ProjectionCompletedEventArgs(List<GpsPoint> projectedPoints, double refLat, double refLon)
        {
            ProjectedPoints = projectedPoints;
            ReferenceLatitude = refLat;
            ReferenceLongitude = refLon;
        }
    }
}
