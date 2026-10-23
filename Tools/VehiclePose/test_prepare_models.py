"""In-memory checks for the retained VXL/HVA port; no runtime/build required."""
import importlib.util
import io
import math
from pathlib import Path
import struct
import sys
import unittest

# Reuse the running script's definitions for --self-test (including InvalidData).
if Path(getattr(sys.modules["__main__"], "__file__", "")).name == "prepare_models.py":
    p = sys.modules["__main__"]
else:
    import prepare_models as p


def fixture(cells=None, size=(2, 1, 2), scale=0.5, minimum=(2, 4, 6), maximum=(6, 10, 14)):
    if cells is None:
        cells = {(0, 0, 0): 16, (1, 0, 0): 0}
    sx, sy, sz = size
    starts, ends = [], []
    spans = bytearray()
    for y in range(sy):
        for x in range(sx):
            if not any((x, y, z) in cells for z in range(sz)):
                starts.append(-1)
                ends.append(-1)
                continue
            starts.append(len(spans))
            z = 0
            while z < sz:
                skip = 0
                while z < sz and (x, y, z) not in cells:
                    skip += 1
                    z += 1
                run = 0
                values = bytearray()
                while z < sz and (x, y, z) in cells:
                    values.extend((cells[(x, y, z)], 7))
                    run += 1
                    z += 1
                spans.extend((skip, run))
                spans.extend(values)
                spans.append(run)
            ends.append(len(spans) - 1)
    columns = sx * sy
    body = struct.pack(f"<{columns}i", *starts) + struct.pack(f"<{columns}i", *ends) + spans
    header = b"Voxel Animation\0" + struct.pack("<4I", 1, 1, 1, len(body)) + bytes(770)
    limb_header = b"VXL label\0".ljust(16, b"\0") + struct.pack("<3I", 0, 1, 2)
    footer = struct.pack("<3If12f6f4B", 0, columns * 4, columns * 8, scale,
                         *p.IDENTITY, *minimum, *maximum, sx, sy, sz, 4)
    return header + limb_header + body + footer


def hva(matrix=p.IDENTITY, frames=None, label="different HVA"):
    matrices = [matrix] if frames is None else frames
    return (bytes(16) + struct.pack("<2I", len(matrices), 1) + label.encode("ascii").ljust(16, b"\0")
            + b"".join(struct.pack("<12f", *m) for m in matrices))


class ParserChecks(unittest.TestCase):
    def test_vxl_checked_span_decoder_and_zero_occupancy(self):
        limbs = p.read_vxl(fixture())
        self.assertEqual((limbs[0].size, limbs[0].count), ((2, 1, 2), 2))
        self.assertEqual(list(limbs[0].colors), [16, 0, 0, 0])
        self.assertEqual(list(limbs[0].occupied), [1, 1, 0, 0])
        self.assertEqual(p.count_faces(limbs), 5)  # Color zero still occludes +X.
        self.assertEqual(len(list(p.face_stream(limbs, None))), 5)

    def test_vxl_multi_span_empty_columns_and_order(self):
        cells = {(0, 0, 0): 16, (0, 0, 2): 17, (1, 1, 1): 18}
        limbs = p.read_vxl(fixture(cells, size=(2, 2, 3)))
        self.assertEqual(limbs[0].count, 3)
        self.assertEqual(p.count_faces(limbs), 18)
        colors = [p.FACE.unpack(face)[-1] for face in p.face_stream(limbs, None)]
        self.assertEqual(colors, [16] * 6 + [18] * 6 + [17] * 6)

    def test_vxl_truncations_and_corruption_rejected(self):
        valid = fixture()
        for length in range(0, len(valid), 13):
            with self.subTest(length=length), self.assertRaises(p.InvalidData):
                p.read_vxl(valid[:length])
        cases = [(28, "<I", 0xffffffff), (830, "<i", 0x7fffffff),
                 (802 + 28 + 16 + 1, "<B", 255), (len(valid) - 4, "<B", 0),
                 (len(valid) - 92 + 12, "<f", float("nan"))]
        for offset, fmt, value in cases:
            malformed = bytearray(valid)
            struct.pack_into(fmt, malformed, offset, value)
            with self.subTest(offset=offset), self.assertRaises(p.InvalidData):
                p.read_vxl(bytes(malformed))
        malformed = bytearray(valid)
        malformed[850] = 2  # First span duplicate count (run is 1).
        with self.assertRaises(p.InvalidData):
            p.read_vxl(bytes(malformed))

    def test_hva_frame_zero_binding_and_checks_all_frames(self):
        second = list(p.IDENTITY)
        second[3] = 99
        matrices = p.read_hva(hva(frames=[p.IDENTITY, second]), 1)
        self.assertEqual(matrices, (p.IDENTITY,))
        self.assertEqual(p.read_hva(hva(label="retail mismatch"), 1), (p.IDENTITY,))
        for malformed in (hva()[:-1], hva([0] * 12), hva(frames=[p.IDENTITY, [0] * 12])):
            with self.assertRaises(p.InvalidData):
                p.read_hva(malformed, 1)
        with self.assertRaises(p.InvalidData):
            p.read_hva(hva(), 2)
        malformed = bytearray(hva())
        struct.pack_into("<f", malformed, 40, float("nan"))
        with self.assertRaises(p.InvalidData):
            p.read_hva(bytes(malformed), 1)

    def test_face_count_budget(self):
        original = p.MAX_FACES
        try:
            p.MAX_FACES = 4
            with self.assertRaises(p.InvalidData):
                p.count_faces(p.read_vxl(fixture()))
            with self.assertRaises(p.InvalidData):
                list(p.face_stream(p.read_vxl(fixture()), None))
        finally:
            p.MAX_FACES = original


class TransformChecks(unittest.TestCase):
    def test_origin_scale_reflection_corner_order_and_layout(self):
        self.assertEqual((p.HEADER.size, p.PART_HEADER.size, p.FACE.size), (24, 5, 61))
        self.assertEqual(p.MAGIC, b"RAVP1\0\0\0")
        face = p.FACE.unpack(next(p.face_stream(p.read_vxl(fixture()), None)))
        self.assertEqual(face[:12], (1, -2, 3, 1, -2, 5, 1, -5, 5, 1, -5, 3))
        self.assertEqual(face[12:], (-1, 0, 0, 16))

    def test_hva_rotation_translation_two_footer_scales_and_y_reflection(self):
        matrix = (0, -1, 0, 2, 1, 0, 0, 3, 0, 0, 2, 4)
        limbs = p.read_vxl(fixture())
        pose = p.read_hva(hva(matrix), 1)
        face = p.FACE.unpack(next(p.face_stream(limbs, pose)))
        self.assertEqual(face[:12], (-1, -5.5, 10, -1, -5.5, 14, -4, -5.5, 14, -4, -5.5, 10))
        self.assertEqual(face[12:], (0, 1, 0, 16))

    def test_inverse_transpose_for_shear(self):
        # B=reflectY*M has X tangent (2,-1,0), Y tangent (1,-3,0).
        matrix = (2, 1, 0, 0, 1, 3, 0, 0, 0, 0, 4, 0)
        normals = p.normal_vectors(matrix)
        for axis in range(3):
            n = normals[axis * 2 + 1]
            self.assertAlmostEqual(sum(v * v for v in n), 1, places=6)
            self.assertEqual(normals[axis * 2], tuple(-v for v in n))
        nx, ny = normals[1], normals[3]
        self.assertAlmostEqual(nx[0] - 3 * nx[1], 0, places=6)
        self.assertAlmostEqual(2 * ny[0] - ny[1], 0, places=6)

    def test_identity_and_absent_hva(self):
        class NoLookup:
            def verified_bytes(self, *args):
                raise AssertionError("Identity pose must not load an HVA")
        limbs = p.read_vxl(fixture())
        self.assertIsNone(p.pose_for({"identityPose": True, "hva": "ignored.hva"}, limbs, NoLookup()))
        self.assertIsNone(p.pose_for({"identityPose": False}, limbs, NoLookup()))
        self.assertEqual(list(p.face_stream(limbs, None)), list(p.face_stream(limbs, (p.IDENTITY,))))


def run_tests():
    suite = unittest.defaultTestLoader.loadTestsFromModule(sys.modules[__name__])
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    sys.exit(run_tests())
