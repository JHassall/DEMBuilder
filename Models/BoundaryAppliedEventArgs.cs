using System;
using System.Collections.Generic;
using GMap.NET;

namespace DEMBuilder.Models
{
    public class BoundaryAppliedEventArgs : EventArgs
    {
        public List<PointLatLng> BoundaryPoints { get; }
        public string FarmName { get; }
        public string FieldName { get; }

        public BoundaryAppliedEventArgs(List<PointLatLng> boundaryPoints, string farmName, string fieldName)
        {
            BoundaryPoints = boundaryPoints;
            FarmName = farmName;
            FieldName = fieldName;
        }
    }
}
