using System;
using System.Collections.Generic;
using System.Linq;

namespace AlterOS.Furniture
{
    public static class FurnitureLayoutCommands
    {
        private const double Epsilon = 1e-7d;

        private sealed class Bounds3
        {
            public double minX, minY, minZ, maxX, maxY, maxZ;
        }

        public static FurnitureLayoutCommandResult Apply(
            FurnitureLayoutCommand command,
            FurnitureLayoutSnapshot current,
            FurniturePlacementEvidence evidence)
        {
            if (command == null || current == null || current.document == null || evidence == null || evidence.catalogue == null)
                return Reject("invalid_request", "Layout command, current snapshot and placement evidence are required.");
            if (!evidence.evidenceComplete)
                return Result(false, "unknown", "evidence_unknown", current, "Placement evidence is incomplete.");
            if (command.homeId != current.document.homeId || command.roomId != current.document.roomId)
                return Reject("wrong_room", "Command home/room does not match the current layout.");
            var currentError = HomeFurnitureRepository.ValidateDocument(current.document).FirstOrDefault(diagnostic => diagnostic.severity == "error");
            if (currentError != null)
                return Result(false, "invalid", "invalid_layout", current, "Current layout is not valid.");

            var document = HomeFurnitureRepository.CloneDocument(current.document);
            switch (command.operation)
            {
                case "place":
                    return Place(command, current, document, evidence);
                case "move":
                case "rotate":
                case "transform":
                    return Transform(command, current, document, evidence);
                case "remove":
                    return Remove(command, current, document, evidence);
                default:
                    return Reject("unsupported_operation", "Layout operation is unsupported.");
            }
        }

        private static FurnitureLayoutCommandResult Place(FurnitureLayoutCommand command, FurnitureLayoutSnapshot current,
            HomeFurnitureLayoutDocument document, FurniturePlacementEvidence evidence)
        {
            if (command.newInstance == null || string.IsNullOrWhiteSpace(command.instanceId) || command.newInstance.instanceId != command.instanceId)
                return Reject("invalid_instance", "Place command must include one new instance with the requested stable ID.");
            if (document.instances.Any(instance => instance.instanceId == command.instanceId))
                return Result(false, "conflict", "instance_exists", current, "Instance ID already exists in the layout.");

            var candidate = HomeFurnitureRepository.CloneInstance(command.newInstance);
            candidate.instanceRevision = 1;
            var validation = ValidatePlacement(candidate, document.instances, evidence);
            if (validation != null) return Result(false, validation.state, validation.reason, current, validation.message);
            document.instances.Add(candidate);
            document.instances = document.instances.OrderBy(instance => instance.instanceId, StringComparer.Ordinal).ToList();
            document.layoutRevision++;
            var invalid = ValidateUpdatedDocument(document);
            if (invalid != null) return Result(false, "rejected", invalid.code, current, invalid.message);
            return Success(document, current.contentHash);
        }

        private static FurnitureLayoutCommandResult Transform(FurnitureLayoutCommand command, FurnitureLayoutSnapshot current,
            HomeFurnitureLayoutDocument document, FurniturePlacementEvidence evidence)
        {
            var instance = document.instances.FirstOrDefault(item => item.instanceId == command.instanceId);
            if (instance == null) return Result(false, "conflict", "instance_missing", current, "Furniture instance no longer exists.");
            if (instance.instanceRevision != command.expectedInstanceRevision)
                return Result(false, "conflict", "stale_revision", current, "Furniture instance revision changed.");
            if (IsReserved(instance.instanceId, evidence.reservedPointRefs))
                return Result(false, "rejected", "point_in_use", current, "Furniture has a point reserved by an active use.");

            if (command.operation == "move" || command.operation == "transform")
            {
                if (command.positionMeters == null) return Reject("invalid_transform", "Move command requires a room-local position.");
                instance.positionMeters = CloneVector(command.positionMeters);
                if (command.support != null) instance.support = HomeFurnitureRepository.CloneSupport(command.support);
            }
            if (command.operation == "rotate" || command.operation == "transform")
            {
                if (command.rotation == null) return Reject("invalid_transform", "Rotate command requires a quaternion.");
                instance.rotation = CloneQuaternion(command.rotation);
                if (command.support != null) instance.support = HomeFurnitureRepository.CloneSupport(command.support);
            }

            if (command.support == null)
            {
                var attachmentFailure = RefreshAttachmentPose(instance, evidence);
                if (attachmentFailure != null)
                    return Result(false, attachmentFailure.state, attachmentFailure.reason, current, attachmentFailure.message);
            }

            var validation = ValidatePlacement(instance, document.instances.Where(item => item.instanceId != instance.instanceId), evidence);
            if (validation != null) return Result(false, validation.state, validation.reason, current, validation.message);
            instance.instanceRevision++;
            document.layoutRevision++;
            var invalid = ValidateUpdatedDocument(document);
            if (invalid != null) return Result(false, "rejected", invalid.code, current, invalid.message);
            return Success(document, current.contentHash);
        }

        private static FurnitureLayoutCommandResult Remove(FurnitureLayoutCommand command, FurnitureLayoutSnapshot current,
            HomeFurnitureLayoutDocument document, FurniturePlacementEvidence evidence)
        {
            var instance = document.instances.FirstOrDefault(item => item.instanceId == command.instanceId);
            if (instance == null) return Result(false, "conflict", "instance_missing", current, "Furniture instance no longer exists.");
            if (instance.instanceRevision != command.expectedInstanceRevision)
                return Result(false, "conflict", "stale_revision", current, "Furniture instance revision changed.");
            if (IsReserved(instance.instanceId, evidence.reservedPointRefs))
                return Result(false, "rejected", "point_in_use", current, "Furniture has a point reserved by an active use.");
            document.instances.Remove(instance);
            document.layoutRevision++;
            var invalid = ValidateUpdatedDocument(document);
            if (invalid != null) return Result(false, "rejected", invalid.code, current, invalid.message);
            return Success(document, current.contentHash);
        }

        private static FurnitureDiagnostic ValidateUpdatedDocument(HomeFurnitureLayoutDocument document)
        {
            return HomeFurnitureRepository.ValidateDocument(document).FirstOrDefault(diagnostic => diagnostic.severity == "error");
        }

        private static PlacementFailure ValidatePlacement(HomeFurnitureLayoutInstance candidate,
            IEnumerable<HomeFurnitureLayoutInstance> otherInstances, FurniturePlacementEvidence evidence)
        {
            var definition = evidence.catalogue.definitions.FirstOrDefault(item => item.definitionId == candidate.definitionId);
            if (definition == null) return new PlacementFailure("rejected", "missing_definition", "Furniture definition is unavailable.");
            if (candidate.scale == null || !NearlyEqual(candidate.scale.x, 1d) || !NearlyEqual(candidate.scale.y, 1d) || !NearlyEqual(candidate.scale.z, 1d))
                return new PlacementFailure("rejected", "invalid_scale", "Version 1 furniture uses fixed unit scale.");
            var support = (evidence.supports ?? new List<FurnitureSupportSnapshot>())
                .FirstOrDefault(item => item.supportId == (candidate.support == null ? string.Empty : candidate.support.supportId));
            if (support == null)
                return new PlacementFailure(evidence.evidenceComplete ? "rejected" : "unknown", "unsupported_support", "Required placement support is unavailable.");
            if (candidate.support.supportKind != support.supportKind || candidate.support.supportFrameRevision != support.revision)
                return new PlacementFailure("rejected", "stale_support", "Support identity or frame revision changed.");
            if (!definition.supportKinds.Contains(support.supportKind))
                return new PlacementFailure("rejected", "unsupported_support", "Furniture definition does not support this placement surface.");
            var poseFailure = ValidateAttachmentPose(candidate, support, evidence.evidenceComplete);
            if (poseFailure != null) return poseFailure;
            if (!ValidRoomBounds(evidence.roomMinimum, evidence.roomMaximum))
                return new PlacementFailure("unknown", "evidence_unknown", "Room bounds are unavailable or invalid.");

            var candidateBounds = TransformBounds(definition.localBounds, candidate.positionMeters, candidate.rotation, candidate.scale);
            if (candidateBounds == null)
                return new PlacementFailure("rejected", "invalid_transform", "Furniture transform is invalid.");
            if (!Contains(evidence.roomMinimum, evidence.roomMaximum, candidateBounds))
                return new PlacementFailure("rejected", "outside_room", "Furniture bounds extend outside the room.");

            if (definition.blocksNavigation)
            {
                foreach (var existing in otherInstances ?? Enumerable.Empty<HomeFurnitureLayoutInstance>())
                {
                    var otherDefinition = evidence.catalogue.definitions.FirstOrDefault(item => item.definitionId == existing.definitionId);
                    if (otherDefinition == null || !otherDefinition.blocksNavigation) continue;
                    var otherBounds = TransformBounds(otherDefinition.localBounds, existing.positionMeters, existing.rotation, existing.scale);
                    if (otherBounds != null && Overlaps(candidateBounds, otherBounds))
                        return new PlacementFailure("rejected", "structural_overlap", "Furniture overlaps another blocking placement.");
                }
            }
            return null;
        }

        private static PlacementFailure RefreshAttachmentPose(HomeFurnitureLayoutInstance instance, FurniturePlacementEvidence evidence)
        {
            if (instance.support == null) return new PlacementFailure("rejected", "missing_support", "Support reference is required.");
            var support = (evidence.supports ?? new List<FurnitureSupportSnapshot>()).FirstOrDefault(item => item.supportId == instance.support.supportId);
            if (support == null) return new PlacementFailure(evidence.evidenceComplete ? "rejected" : "unknown", "unsupported_support", "Required support is unavailable.");
            FurnitureVector3 supportPosition;
            FurnitureQuaternion supportRotation;
            var frameFailure = ResolveSupportFrame(support, evidence.evidenceComplete, out supportPosition, out supportRotation);
            if (frameFailure != null) return frameFailure;

            var inverse = Conjugate(supportRotation);
            var offset = new FurnitureVector3
            {
                x = instance.positionMeters.x - supportPosition.x,
                y = instance.positionMeters.y - supportPosition.y,
                z = instance.positionMeters.z - supportPosition.z
            };
            instance.support.localPositionMeters = Rotate(inverse, offset);
            instance.support.localRotation = Multiply(inverse, instance.rotation);
            if (instance.support.localPositionMeters == null || instance.support.localRotation == null)
                return new PlacementFailure("rejected", "invalid_transform", "Transform could not be expressed in its support frame.");
            return null;
        }

        private static PlacementFailure ValidateAttachmentPose(HomeFurnitureLayoutInstance instance, FurnitureSupportSnapshot support, bool evidenceComplete)
        {
            if (instance.support.localPositionMeters == null || instance.support.localRotation == null)
                return new PlacementFailure("rejected", "missing_attachment_pose", "Support-local attachment pose is required.");
            FurnitureVector3 supportPosition;
            FurnitureQuaternion supportRotation;
            var frameFailure = ResolveSupportFrame(support, evidenceComplete, out supportPosition, out supportRotation);
            if (frameFailure != null) return frameFailure;
            var localOffset = Rotate(supportRotation, instance.support.localPositionMeters);
            if (localOffset == null) return new PlacementFailure("rejected", "invalid_transform", "Support-local pose is invalid.");
            var expectedPosition = new FurnitureVector3
            {
                x = supportPosition.x + localOffset.x,
                y = supportPosition.y + localOffset.y,
                z = supportPosition.z + localOffset.z
            };
            var positionError = Distance(expectedPosition, instance.positionMeters);
            var expectedRotation = Multiply(supportRotation, instance.support.localRotation);
            var rotationError = QuaternionDifferenceDegrees(expectedRotation, instance.rotation);
            if (positionError > 0.001d || rotationError > 0.1d)
                return new PlacementFailure("rejected", "support_pose_mismatch", "Room-local transform does not match its support-local attachment pose.");
            return null;
        }

        private static PlacementFailure ResolveSupportFrame(FurnitureSupportSnapshot support, bool evidenceComplete,
            out FurnitureVector3 position, out FurnitureQuaternion rotation)
        {
            if (support.positionMeters != null && support.rotation != null)
            {
                position = support.positionMeters;
                rotation = support.rotation;
                return null;
            }
            if (support.supportKind == "roomFloor" && support.supportId == "room.floor")
            {
                position = new FurnitureVector3();
                rotation = new FurnitureQuaternion { x = 0, y = 0, z = 0, w = 1 };
                return null;
            }
            position = null;
            rotation = null;
            return new PlacementFailure("unknown", "support_frame_unknown", evidenceComplete
                ? "Support transform is missing from placement evidence."
                : "Support transform evidence is incomplete.");
        }

        private static bool IsReserved(string instanceId, IEnumerable<FurniturePointReference> refs)
        {
            return (refs ?? Enumerable.Empty<FurniturePointReference>()).Any(reference => reference != null && reference.instanceId == instanceId);
        }

        private static Bounds3 TransformBounds(FurnitureBounds localBounds, FurnitureVector3 position, FurnitureQuaternion rotation, FurnitureVector3 scale)
        {
            if (localBounds == null || localBounds.minimum == null || localBounds.maximum == null || position == null || rotation == null || scale == null)
                return null;
            var values = new List<FurnitureVector3>();
            foreach (var x in new[] { localBounds.minimum.x, localBounds.maximum.x })
            foreach (var y in new[] { localBounds.minimum.y, localBounds.maximum.y })
            foreach (var z in new[] { localBounds.minimum.z, localBounds.maximum.z })
            {
                var scaled = new FurnitureVector3 { x = x * scale.x, y = y * scale.y, z = z * scale.z };
                var rotated = Rotate(rotation, scaled);
                if (rotated == null) return null;
                values.Add(new FurnitureVector3 { x = position.x + rotated.x, y = position.y + rotated.y, z = position.z + rotated.z });
            }
            return new Bounds3
            {
                minX = values.Min(value => value.x), minY = values.Min(value => value.y), minZ = values.Min(value => value.z),
                maxX = values.Max(value => value.x), maxY = values.Max(value => value.y), maxZ = values.Max(value => value.z)
            };
        }

        private static FurnitureVector3 Rotate(FurnitureQuaternion quaternion, FurnitureVector3 value)
        {
            var magnitude = Math.Sqrt(quaternion.x * quaternion.x + quaternion.y * quaternion.y + quaternion.z * quaternion.z + quaternion.w * quaternion.w);
            if (!IsFinite(magnitude) || Math.Abs(magnitude - 1d) > 0.001d) return null;
            var x = quaternion.x / magnitude; var y = quaternion.y / magnitude; var z = quaternion.z / magnitude; var w = quaternion.w / magnitude;
            var uvX = y * value.z - z * value.y; var uvY = z * value.x - x * value.z; var uvZ = x * value.y - y * value.x;
            var uuvX = y * uvZ - z * uvY; var uuvY = z * uvX - x * uvZ; var uuvZ = x * uvY - y * uvX;
            return new FurnitureVector3
            {
                x = value.x + 2d * (w * uvX + uuvX),
                y = value.y + 2d * (w * uvY + uuvY),
                z = value.z + 2d * (w * uvZ + uuvZ)
            };
        }

        private static FurnitureQuaternion Conjugate(FurnitureQuaternion value)
        {
            var magnitude = Math.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            if (!IsFinite(magnitude) || magnitude <= 1e-12d) return null;
            return new FurnitureQuaternion { x = -value.x / magnitude, y = -value.y / magnitude, z = -value.z / magnitude, w = value.w / magnitude };
        }

        private static FurnitureQuaternion Multiply(FurnitureQuaternion left, FurnitureQuaternion right)
        {
            if (left == null || right == null) return null;
            var value = new FurnitureQuaternion
            {
                x = left.w * right.x + left.x * right.w + left.y * right.z - left.z * right.y,
                y = left.w * right.y - left.x * right.z + left.y * right.w + left.z * right.x,
                z = left.w * right.z + left.x * right.y - left.y * right.x + left.z * right.w,
                w = left.w * right.w - left.x * right.x - left.y * right.y - left.z * right.z
            };
            var magnitude = Math.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            if (!IsFinite(magnitude) || magnitude <= 1e-12d) return null;
            value.x /= magnitude; value.y /= magnitude; value.z /= magnitude; value.w /= magnitude;
            return value;
        }

        private static double QuaternionDifferenceDegrees(FurnitureQuaternion left, FurnitureQuaternion right)
        {
            if (left == null || right == null) return double.PositiveInfinity;
            var leftLength = Math.Sqrt(left.x * left.x + left.y * left.y + left.z * left.z + left.w * left.w);
            var rightLength = Math.Sqrt(right.x * right.x + right.y * right.y + right.z * right.z + right.w * right.w);
            if (!IsFinite(leftLength) || !IsFinite(rightLength) || leftLength <= 1e-12d || rightLength <= 1e-12d)
                return double.PositiveInfinity;
            var dot = Math.Abs(left.x * right.x + left.y * right.y + left.z * right.z + left.w * right.w) / (leftLength * rightLength);
            dot = Math.Max(-1d, Math.Min(1d, dot));
            return 2d * Math.Acos(dot) * 180d / Math.PI;
        }

        private static double Distance(FurnitureVector3 left, FurnitureVector3 right)
        {
            if (left == null || right == null) return double.PositiveInfinity;
            var x = left.x - right.x; var y = left.y - right.y; var z = left.z - right.z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static bool Contains(FurnitureVector3 minimum, FurnitureVector3 maximum, Bounds3 bounds)
        {
            return bounds.minX >= minimum.x - Epsilon && bounds.minY >= minimum.y - Epsilon && bounds.minZ >= minimum.z - Epsilon &&
                bounds.maxX <= maximum.x + Epsilon && bounds.maxY <= maximum.y + Epsilon && bounds.maxZ <= maximum.z + Epsilon;
        }

        private static bool Overlaps(Bounds3 left, Bounds3 right)
        {
            return left.minX < right.maxX - Epsilon && left.maxX > right.minX + Epsilon &&
                left.minY < right.maxY - Epsilon && left.maxY > right.minY + Epsilon &&
                left.minZ < right.maxZ - Epsilon && left.maxZ > right.minZ + Epsilon;
        }

        private static bool ValidRoomBounds(FurnitureVector3 minimum, FurnitureVector3 maximum)
        {
            return minimum != null && maximum != null && IsFinite(minimum.x) && IsFinite(minimum.y) && IsFinite(minimum.z) &&
                IsFinite(maximum.x) && IsFinite(maximum.y) && IsFinite(maximum.z) &&
                minimum.x < maximum.x && minimum.y < maximum.y && minimum.z < maximum.z;
        }

        private static FurnitureVector3 CloneVector(FurnitureVector3 value)
        {
            return new FurnitureVector3 { x = value.x, y = value.y, z = value.z };
        }

        private static FurnitureQuaternion CloneQuaternion(FurnitureQuaternion value)
        {
            return new FurnitureQuaternion { x = value.x, y = value.y, z = value.z, w = value.w };
        }

        private static FurnitureLayoutCommandResult Success(HomeFurnitureLayoutDocument document, string previousHash)
        {
            return new FurnitureLayoutCommandResult
            {
                success = true,
                state = "applied",
                reasonCode = string.Empty,
                snapshot = new FurnitureLayoutSnapshot { document = document, contentHash = null },
                expectedSaveHash = previousHash
            };
        }

        private static FurnitureLayoutCommandResult Reject(string reason, string message)
        {
            return Result(false, "rejected", reason, null, message);
        }

        private static FurnitureLayoutCommandResult Result(bool success, string state, string reason, FurnitureLayoutSnapshot snapshot, string message)
        {
            return new FurnitureLayoutCommandResult
            {
                success = success,
                state = state,
                reasonCode = reason,
                snapshot = snapshot,
                diagnostic = new FurnitureDiagnostic(reason, state == "unknown" ? "warning" : "error", "command", message)
            };
        }

        private static bool NearlyEqual(double left, double right)
        {
            return Math.Abs(left - right) <= 1e-9d;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class PlacementFailure
        {
            public readonly string state;
            public readonly string reason;
            public readonly string message;
            public PlacementFailure(string state, string reason, string message)
            {
                this.state = state;
                this.reason = reason;
                this.message = message;
            }
        }
    }
}
