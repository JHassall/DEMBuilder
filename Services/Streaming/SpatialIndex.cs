using DEMBuilder.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DEMBuilder.Services.Streaming
{
    /// <summary>
    /// High-performance spatial index for fast GPS point lookups
    /// Uses a quad-tree structure for O(log n) spatial queries
    /// </summary>
    public class SpatialIndex
    {
        private readonly QuadTreeNode _root;
        private readonly double _minX, _minY, _maxX, _maxY;
        private int _totalPoints = 0;

        public SpatialIndex(double minX = -180, double minY = -90, double maxX = 180, double maxY = 90)
        {
            _minX = minX;
            _minY = minY;
            _maxX = maxX;
            _maxY = maxY;
            _root = new QuadTreeNode(minX, minY, maxX, maxY);
        }

        public void AddPoint(GpsPoint point)
        {
            _root.Insert(point);
            _totalPoints++;
        }

        public List<GpsPoint> QueryRegion(double minX, double minY, double maxX, double maxY)
        {
            var results = new List<GpsPoint>();
            _root.QueryRegion(minX, minY, maxX, maxY, results);
            return results;
        }

        public List<GpsPoint> QueryRadius(double centerX, double centerY, double radius)
        {
            var results = new List<GpsPoint>();
            _root.QueryRadius(centerX, centerY, radius, results);
            return results;
        }

        public GpsPoint? FindNearestPoint(double x, double y)
        {
            return _root.FindNearest(x, y);
        }

        public int TotalPoints => _totalPoints;

        public SpatialIndexStats GetStats()
        {
            var stats = new SpatialIndexStats();
            _root.GatherStats(stats);
            stats.TotalPoints = _totalPoints;
            return stats;
        }
    }

    internal class QuadTreeNode
    {
        private const int MaxPointsPerNode = 100;
        private const int MaxDepth = 10;

        private readonly double _minX, _minY, _maxX, _maxY;
        private readonly int _depth;
        private List<GpsPoint>? _points;
        private QuadTreeNode[]? _children;

        public QuadTreeNode(double minX, double minY, double maxX, double maxY, int depth = 0)
        {
            _minX = minX;
            _minY = minY;
            _maxX = maxX;
            _maxY = maxY;
            _depth = depth;
            _points = new List<GpsPoint>();
        }

        public void Insert(GpsPoint point)
        {
            if (!Contains(point.Easting, point.Northing))
                return;

            if (_children == null)
            {
                _points!.Add(point);

                // Split if we have too many points and haven't reached max depth
                if (_points.Count > MaxPointsPerNode && _depth < MaxDepth)
                {
                    Split();
                }
            }
            else
            {
                // Insert into appropriate child
                foreach (var child in _children)
                {
                    if (child.Contains(point.Easting, point.Northing))
                    {
                        child.Insert(point);
                        break;
                    }
                }
            }
        }

        public void QueryRegion(double minX, double minY, double maxX, double maxY, List<GpsPoint> results)
        {
            if (!Intersects(minX, minY, maxX, maxY))
                return;

            if (_children == null)
            {
                // Leaf node - check all points
                foreach (var point in _points!)
                {
                    if (point.Easting >= minX && point.Easting <= maxX &&
                        point.Northing >= minY && point.Northing <= maxY)
                    {
                        results.Add(point);
                    }
                }
            }
            else
            {
                // Internal node - query children
                foreach (var child in _children)
                {
                    child.QueryRegion(minX, minY, maxX, maxY, results);
                }
            }
        }

        public void QueryRadius(double centerX, double centerY, double radius, List<GpsPoint> results)
        {
            double radiusSquared = radius * radius;

            if (!IntersectsCircle(centerX, centerY, radius))
                return;

            if (_children == null)
            {
                // Leaf node - check all points
                foreach (var point in _points!)
                {
                    double dx = point.Easting - centerX;
                    double dy = point.Northing - centerY;
                    if (dx * dx + dy * dy <= radiusSquared)
                    {
                        results.Add(point);
                    }
                }
            }
            else
            {
                // Internal node - query children
                foreach (var child in _children)
                {
                    child.QueryRadius(centerX, centerY, radius, results);
                }
            }
        }

        public GpsPoint? FindNearest(double x, double y)
        {
            GpsPoint? nearest = null;
            double nearestDistSq = double.MaxValue;
            FindNearestRecursive(x, y, ref nearest, ref nearestDistSq);
            return nearest;
        }

        private void FindNearestRecursive(double x, double y, ref GpsPoint? nearest, ref double nearestDistSq)
        {
            if (_children == null)
            {
                // Leaf node - check all points
                foreach (var point in _points!)
                {
                    double dx = point.Easting - x;
                    double dy = point.Northing - y;
                    double distSq = dx * dx + dy * dy;
                    
                    if (distSq < nearestDistSq)
                    {
                        nearest = point;
                        nearestDistSq = distSq;
                    }
                }
            }
            else
            {
                // Sort children by distance to query point
                var childDistances = _children
                    .Select((child, index) => new { Child = child, Index = index, Distance = child.DistanceToPoint(x, y) })
                    .OrderBy(cd => cd.Distance)
                    .ToArray();

                foreach (var cd in childDistances)
                {
                    // Skip if this child can't possibly contain a closer point
                    if (cd.Distance * cd.Distance > nearestDistSq)
                        break;

                    cd.Child.FindNearestRecursive(x, y, ref nearest, ref nearestDistSq);
                }
            }
        }

        public void GatherStats(SpatialIndexStats stats)
        {
            stats.TotalNodes++;
            
            if (_children == null)
            {
                stats.LeafNodes++;
                stats.MaxPointsInLeaf = Math.Max(stats.MaxPointsInLeaf, _points!.Count);
                stats.MinPointsInLeaf = Math.Min(stats.MinPointsInLeaf, _points.Count);
                stats.MaxDepth = Math.Max(stats.MaxDepth, _depth);
            }
            else
            {
                stats.InternalNodes++;
                foreach (var child in _children)
                {
                    child.GatherStats(stats);
                }
            }
        }

        private void Split()
        {
            double midX = (_minX + _maxX) / 2;
            double midY = (_minY + _maxY) / 2;

            _children = new QuadTreeNode[4];
            _children[0] = new QuadTreeNode(_minX, _minY, midX, midY, _depth + 1); // SW
            _children[1] = new QuadTreeNode(midX, _minY, _maxX, midY, _depth + 1);  // SE
            _children[2] = new QuadTreeNode(_minX, midY, midX, _maxY, _depth + 1);  // NW
            _children[3] = new QuadTreeNode(midX, midY, _maxX, _maxY, _depth + 1);  // NE

            // Redistribute points to children
            foreach (var point in _points!)
            {
                foreach (var child in _children)
                {
                    if (child.Contains(point.Easting, point.Northing))
                    {
                        child.Insert(point);
                        break;
                    }
                }
            }

            // Clear points from this node (now internal)
            _points = null;
        }

        private bool Contains(double x, double y)
        {
            return x >= _minX && x <= _maxX && y >= _minY && y <= _maxY;
        }

        private bool Intersects(double minX, double minY, double maxX, double maxY)
        {
            return !(_maxX < minX || _minX > maxX || _maxY < minY || _minY > maxY);
        }

        private bool IntersectsCircle(double centerX, double centerY, double radius)
        {
            double closestX = Math.Max(_minX, Math.Min(centerX, _maxX));
            double closestY = Math.Max(_minY, Math.Min(centerY, _maxY));
            
            double dx = centerX - closestX;
            double dy = centerY - closestY;
            
            return dx * dx + dy * dy <= radius * radius;
        }

        private double DistanceToPoint(double x, double y)
        {
            double dx = Math.Max(0, Math.Max(_minX - x, x - _maxX));
            double dy = Math.Max(0, Math.Max(_minY - y, y - _maxY));
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public class SpatialIndexStats
    {
        public int TotalPoints { get; set; }
        public int TotalNodes { get; set; }
        public int LeafNodes { get; set; }
        public int InternalNodes { get; set; }
        public int MaxPointsInLeaf { get; set; }
        public int MinPointsInLeaf { get; set; } = int.MaxValue;
        public int MaxDepth { get; set; }

        public double AveragePointsPerLeaf => LeafNodes > 0 ? (double)TotalPoints / LeafNodes : 0;
        public double TreeEfficiency => TotalNodes > 0 ? (double)LeafNodes / TotalNodes : 0;
    }
}
