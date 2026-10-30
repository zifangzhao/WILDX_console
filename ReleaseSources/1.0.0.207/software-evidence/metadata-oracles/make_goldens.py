"""Independent schema3 metadata and ordering oracles; never accesses hardware."""
from pathlib import Path
import hashlib, json, struct, zlib

ROOT = Path(__file__).resolve().parent
BASE = Path('C:/Frank/Code/XE_NP/_artifacts/grid16_codec_stage')
DOC = Path('D:/Code/WILDX_console/WILDX_console/Note/NP2_SD_GRID16_APPEND_WIRE.md')
EXPECTED_DOC = '4E5578FD874A6A3C868A48B23F3F15654809CA3FE47B67CF8172D856F203B09F'
assert hashlib.sha256(DOC.read_bytes()).hexdigest().upper() == EXPECTED_DOC
U = lambda b, o: struct.unpack_from('<I', b, o)[0]
Q = lambda b, o: struct.unpack_from('<Q', b, o)[0]
def put(b, o, n): struct.pack_into('<I', b, o, n)
def putq(b, o, n): struct.pack_into('<Q', b, o, n)
def seal(b): put(b, 508, zlib.crc32(b[:508]))
def digest(b): return hashlib.sha256(b).hexdigest().upper()

base_d = BASE.joinpath('descriptor.bin').read_bytes()
base_f = BASE.joinpath('footer.bin').read_bytes()
assert len(base_d) == len(base_f) == 512
assert all(U(b, 508) == zlib.crc32(b[:508]) for b in (base_d, base_f))
assert (U(base_d, 8), U(base_d, 24), U(base_d, 16), U(base_d, 220)) == (16, 7, 20000, 11)
descriptor = bytearray(base_d)
put(descriptor, 64, U(descriptor, 60) + 16 * 365)  # 64 logical NN record capacity.
put(descriptor, 68, 32768)
put(descriptor, 216, 3)
put(descriptor, 224, 1)
seal(descriptor)

def metadata(ids, state, admitted, p=None, h=None):
    n = len(ids) * 4
    logical = set(ids)
    prefix = 0
    while prefix in logical: prefix += 1
    expected_p = prefix * 4
    expected_h = (max(ids) + 1) * 4 if ids else 0
    f = bytearray(base_f)
    f[8:100] = descriptor[8:100]
    f[208:256] = descriptor[208:256]
    f[256:508] = bytes(252)
    put(f, 100, state)
    put(f, 104, 0 if state == 3 else 1)
    put(f, 108, n)
    put(f, 112, len(ids))
    put(f, 116, U(descriptor, 60) + len(ids) * 365)
    putq(f, 120, n * 40960)
    putq(f, 256, len(ids) * 365)
    putq(f, 264, len(ids))
    put(f, 372, len(ids))  # Metadata-only fixtures declare valid empty AUX.
    putq(f, 376, 0xffffffffffffffff)
    put(f, 408, expected_p if p is None else p)
    put(f, 412, expected_h if h is None else h)
    put(f, 416, 0xffffffff if admitted is None else admitted)
    put(f, 420, 0 if admitted is None else 1)
    seal(f)
    return f, expected_p, expected_h

cases = []
def add(name, ids, state=4, admitted=12, expected='accept', p=None, h=None, mutate=None, crc_bad=False):
    d = bytearray(descriptor)
    f, actual_p, actual_h = metadata(ids, state, admitted, p, h)
    if mutate: mutate(d, f)
    seal(d); seal(f)
    if crc_bad: f[508] ^= 1
    target = ROOT / name
    target.mkdir(exist_ok=False)
    target.joinpath('descriptor.bin').write_bytes(d)
    target.joinpath('footer.bin').write_bytes(f)
    firsts = [g * 4 for g in ids]
    logical_order = sorted(range(len(ids)), key=lambda i: ids[i])
    cases.append(dict(name=name, expected=expected, physical_logical_group_ids=ids,
        physical_original_nn_first=firsts, physical_original_nn_parent=[x + 3 for x in firsts],
        logical_read_physical_groups=logical_order, actual_index_prefix=actual_p,
        actual_index_high_water=actual_h, physical_nn_count=len(ids) * 4,
        descriptor_sha256=digest(d), footer_sha256=digest(f),
        payload_present=False, scope='metadata/header-index oracle, not a decodable recording'))

add('complete_0_2_1', [0, 2, 1], state=3)
add('partial_0_2_known', [0, 2])
add('partial_0_2_unknown', [0, 2], admitted=None)
add('partial_leading_hole', [2])
add('partial_unsealed_admitted', [0], admitted=6)
add('bad_index_prefix', [0, 2], p=8, expected='reject_index_coverage')
add('bad_index_high_water', [0, 2], h=16, expected='reject_index_coverage')
add('bad_complete_coverage', [0, 2], state=3, expected='reject_metadata_complete')
add('duplicate_logical_group', [0, 2, 2], expected='reject_index_duplicate')
add('bad_unknown_A_sentinel', [0, 2], admitted=None, expected='reject_metadata',
    mutate=lambda d, f: put(f, 416, 12))
add('bad_known_A_capacity', [0, 2], expected='reject_metadata',
    mutate=lambda d, f: put(f, 416, 0xffffffff))
add('bad_reserved_404', [0, 2], expected='reject_metadata', mutate=lambda d, f: put(f, 404, 1))
add('bad_reserved_424', [0, 2], expected='reject_metadata', mutate=lambda d, f: put(f, 424, 1))
add('bad_schema2_order1', [0, 2], expected='reject_metadata',
    mutate=lambda d, f: (put(d, 216, 2), put(f, 216, 2)))
add('bad_crc', [0, 2], expected='reject_crc', crc_bad=True)

oracle = dict(contract_sha256=EXPECTED_DOC, source='independent Python construction from native schema1 fixture',
    baseline={str(BASE / 'descriptor.bin'): digest(base_d), str(BASE / 'footer.bin'): digest(base_f)},
    neural_payload_present=False, hardware_access=False, cases=cases,
    aux_order_oracle=dict(scope='separate abstract ordering oracle; metadata fixtures above contain empty AUX',
        physical_nn_group_ids=[0, 2, 1], physical_wrapper_parents=[3, 11, 7],
        physical_nsg_first_slots=[0, 12, 24],
        incorrect_whole_group_sort_first_slots=[0, 24, 12],
        required='feed AUX in original physical admission order; decode NN in logical order'))
ROOT.joinpath('expected.json').write_text(json.dumps(oracle, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(cases=len(cases), metadata_bytes=len(cases)*1024,
    expected_sha256=digest(ROOT.joinpath('expected.json').read_bytes()), hardware_access=False)))
