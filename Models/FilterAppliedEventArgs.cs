using System;
using System.Collections.Generic;

namespace DEMBuilder.Models
{
    public class FilterAppliedEventArgs : EventArgs
    {
        public List<GpsPoint> FilteredPoints { get; }
        public List<GpsPoint> ExcludedPoints { get; }

        public FilterAppliedEventArgs(List<GpsPoint> filteredPoints, List<GpsPoint> excludedPoints)
        {
            FilteredPoints = filteredPoints;
            ExcludedPoints = excludedPoints;
        }
    }
}
