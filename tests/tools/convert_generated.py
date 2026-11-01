"""Converts taffy's Chrome-generated XML conformance tests into xunit facts.

Usage: python tests/tools/convert_generated.py <taffy>/tests/xml tests/TaffySharp.Tests/Generated

Each tests/xml/<group>/<name>.xml becomes one [Fact] <name> in Generated/<Group>/<name>.cs. Style values are parsed
with the rules of taffy's `parse` feature (the FromCss impls in src/style), as upstream tests/xml.rs does at runtime.
Values those parsers reject (where the upstream test would panic) make the test skipped and counted.
"""
import collections
import os
import re
import shutil
import struct
import sys
import xml.etree.ElementTree as ET

GROUPS = {'block': 'Block', 'blockflex': 'BlockFlex', 'blockgrid': 'BlockGrid', 'contain': 'Contain', 'flex': 'Flex',
          'float': 'Float', 'grid': 'Grid', 'gridflex': 'GridFlex', 'leaf': 'Leaf'}


class Skip(Exception):
    pass


def f32(value):
    return struct.unpack('<f', struct.pack('<f', value))[0]


def lit(value):
    """Shortest C# float literal that round-trips the f32 value."""
    v = f32(float(value))
    if v == int(v) and abs(v) < 1e7:
        return ('-' if str(v).startswith('-') else '') + '%df' % abs(int(v))
    for precision in range(1, 10):
        s = '%.*g' % (precision, v)
        if f32(float(s)) == v:
            return s + 'f'
    raise AssertionError(value)


def parse_f32(text):
    """Rust str::parse::<f32>."""
    if text is None or not re.fullmatch(r'[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?', text.strip()):
        raise Skip('number %r' % text)
    return float(text)


def cs_string(text):
    out = []
    for ch in text:
        if ch in '"\\':
            out.append('\\' + ch)
        elif 32 <= ord(ch) < 127:
            out.append(ch)
        else:
            out.append('\\u%04X' % ord(ch))
    return '"' + ''.join(out) + '"'


def pascal(keyword):
    return ''.join(part.capitalize() for part in keyword.split('-'))


# --- A minimal CSS tokenizer and parser mirroring the cssparser calls of taffy's FromCss impls ---

TOKEN = re.compile(r'\s+|(?P<num>[+-]?(?:\d*\.\d+|\d+)(?:[eE][+-]?\d+)?)(?P<unit>%|[a-zA-Z]+)?'
                   r'|(?P<ident>-?[a-zA-Z_][\w-]*)(?P<fn>\()?|(?P<punct>[\[\](),])')


class Parser:
    def __init__(self, text):
        self.tokens = []
        pos = 0
        while pos < len(text):
            m = TOKEN.match(text, pos)
            if not m or m.end() == pos:
                raise Skip('token in %r' % text)
            pos = m.end()
            if m.group('num') is not None:
                num, unit = m.group('num'), m.group('unit')
                if unit == '%':
                    self.tokens.append(('pct', float(num) / 100))
                elif unit:
                    self.tokens.append(('dim', float(num), unit))
                else:
                    is_int = re.fullmatch(r'[+-]?\d+', num) is not None
                    int_value = max(-2 ** 31, min(2 ** 31 - 1, int(num))) if is_int else None
                    self.tokens.append(('num', float(num), int_value))
            elif m.group('ident') is not None:
                self.tokens.append(('fn' if m.group('fn') else 'ident', m.group('ident')))
            elif m.group('punct') is not None:
                self.tokens.append((m.group('punct'),))
        self.pos = 0

    def exhausted(self):
        # The end of the input or of the current nested block
        return self.pos >= len(self.tokens) or self.tokens[self.pos][0] in (')', ']')

    def next(self):
        if self.exhausted():
            raise Skip('unexpected end')
        self.pos += 1
        return self.tokens[self.pos - 1]

    def expect(self, kind):
        token = self.next()
        if token[0] != kind:
            raise Skip('expected %s, got %r' % (kind, token))
        return token

    def try_parse(self, fn):
        start = self.pos
        try:
            return fn()
        except Skip:
            self.pos = start
            return None

    def nested(self, fn, close=')'):
        value = fn()
        if not self.exhausted() or self.pos >= len(self.tokens) or self.tokens[self.pos][0] != close:
            raise Skip('trailing tokens in block')
        self.pos += 1
        return value


def parse_entirely(text, fn):
    p = Parser(text)
    value = fn(p)
    if p.pos != len(p.tokens):
        raise Skip('trailing tokens in %r' % text)
    return value


def length_percentage(p, ty, extra_idents=()):
    token = p.next()
    if token[0] == 'pct':
        return '%s.Percent(%s)' % (ty, lit(token[1]))
    if token[0] == 'dim' and token[2] == 'px':
        return '%s.Length(%s)' % (ty, lit(token[1]))
    if token[0] == 'ident' and token[1] in extra_idents:
        return '%s.%s' % (ty, pascal(token[1]))
    raise Skip('%s %r' % (ty, token))


def fit_content_fn(p, ty):
    def inner():
        token = p.next()
        if token[0] == 'pct':
            return '%s.FitContentPercent(%s)' % (ty, lit(token[1]))
        if token[0] == 'dim' and token[2] == 'px':
            return '%s.FitContentPx(%s)' % (ty, lit(token[1]))
        raise Skip('fit-content %r' % (token,))
    return p.nested(inner)


def dimension(p):
    if p.tokens[p.pos:p.pos + 1] == [('fn', 'fit-content')]:
        p.next()
        return fit_content_fn(p, 'Dimension')
    return length_percentage(p, 'Dimension', ('auto', 'min-content', 'max-content', 'fit-content', 'stretch', 'content'))


def max_track(p):
    token = p.tokens[p.pos] if p.pos < len(p.tokens) else None
    if token == ('fn', 'fit-content'):
        p.next()
        return fit_content_fn(p, 'MaxTrackSizingFunction')
    if token and token[0] == 'dim' and token[2] == 'fr' and not str(token[1]).startswith('-'):
        p.next()
        return 'MaxTrackSizingFunction.Fr(%s)' % lit(token[1])
    return length_percentage(p, 'MaxTrackSizingFunction', ('auto', 'min-content', 'max-content'))


def min_track(p):
    return length_percentage(p, 'MinTrackSizingFunction', ('auto', 'min-content', 'max-content'))


def track_sizing_function(p):
    def minmax():
        if p.next() != ('fn', 'minmax'):
            raise Skip('not minmax')

        def inner():
            mn = min_track(p)
            p.expect(',')
            return 'new TrackSizingFunction(%s, %s)' % (mn, max_track(p))
        return p.nested(inner)
    return p.try_parse(minmax) or 'Track(%s)' % max_track(p)


def line_names(p):
    def inner():
        p.expect('[')

        def names():
            out = []
            while not p.exhausted():
                out.append(p.expect('ident')[1])
            return out
        return p.nested(names, ']')
    return p.try_parse(inner) or []


def template_tracks(p, track):
    """GridTemplateTracks: one line name group per line (empty when the line has no [...])."""
    tracks, names = [], [line_names(p)]
    while not p.exhausted():
        tracks.append(track(p))
        names.append(line_names(p))
    if not tracks:
        raise Skip('empty track list')
    return tracks, names


def list_expr(items, elem_type):
    """A list initializer; long lists are run-length encoded, as one huge initializer overflows the test's stack."""
    if len(items) <= 100:
        return 'new() { %s }' % ', '.join(items)
    runs = []
    for item in items:
        if runs and runs[-1][0] == item:
            runs[-1][1] += 1
        else:
            runs.append([item, 1])
    item_type = lambda item: re.sub(r'^new\(\)', 'new List<string>()', re.sub(r'^new\(\) \{', 'new List<string> {', item))
    parts = ['Enumerable.Range(0, %d).Select(_ => (%s)%s)' % (n, elem_type, item_type(item)) for item, n in runs]
    return 'new List<%s>(%s)' % (elem_type, ''.join(parts[:1] + ['.Concat(%s)' % part for part in parts[1:]]))


def names_expr(names):
    return list_expr(['new() { %s }' % ', '.join(cs_string(n) for n in group) if group else 'new()' for group in names],
                     'List<string>')


def saturate(value, lo, hi):
    return max(lo, min(hi, value))


def template_component(p):
    def repeat():
        if p.next() != ('fn', 'repeat'):
            raise Skip('not repeat')

        def inner():
            token = p.next()
            if token[0] == 'num' and token[2] is not None and token[2] > 0:
                count = 'RepetitionCount.Count(%d)' % saturate(token[2], 0, 65535)
            elif token in (('ident', 'auto-fit'), ('ident', 'auto-fill')):
                count = 'RepetitionCount.' + pascal(token[1])
            else:
                raise Skip('repetition count %r' % (token,))
            p.expect(',')
            tracks, names = template_tracks(p, track_sizing_function)
            return 'GridTemplateComponent.Repeat(new GridTemplateRepetition(%s, %s, %s))' % (
                count, list_expr(tracks, 'TrackSizingFunction'), names_expr(names))
        return p.nested(inner)
    return p.try_parse(repeat) or track_sizing_function(p)


def grid_placement(p):
    span, number, ident = False, None, None
    while not p.exhausted():
        token = p.next()
        if token[0] == 'ident' and token[1] == 'auto':
            if span or number is not None or ident is not None or not p.exhausted():
                raise Skip('placement auto')
            return 'GridPlacement.Auto'
        elif token == ('ident', 'span'):
            if span:
                raise Skip('placement span')
            span = True
        elif token[0] == 'ident':
            if ident is not None:
                raise Skip('placement ident')
            ident = token[1]
        elif token[0] == 'num' and token[2] is not None and token[2] != 0:
            if number is not None:
                raise Skip('placement number')
            number = token[2]
        else:
            raise Skip('placement %r' % (token,))
    if span:
        n = saturate(number or 0, 0, 65535)
        return 'GridPlacement.NamedSpan(%s, %d)' % (cs_string(ident), n) if ident else 'GridPlacement.Span(%d)' % n
    if number is not None:
        n = saturate(number, -32768, 32767)
        return 'GridPlacement.NamedLine(%s, %d)' % (cs_string(ident), n) if ident else 'GridPlacement.Line(%d)' % n
    if ident is not None:
        return 'GridPlacement.NamedLine(%s, 0)' % cs_string(ident)
    raise Skip('empty placement')


ALIGN_ITEMS = ['normal', 'start', 'end', 'flex-start', 'flex-end', 'self-start', 'self-end', 'center', 'baseline', 'stretch']
ALIGN_CONTENT = ['normal', 'start', 'end', 'flex-start', 'flex-end', 'center', 'stretch', 'space-between', 'space-evenly',
                 'space-around']
SAFE_ITEMS = ['start', 'end', 'flex-start', 'flex-end', 'self-start', 'self-end', 'center']
SAFE_CONTENT = ['start', 'end', 'flex-start', 'flex-end', 'center']


def alignment(ty, keywords, safe_keywords):
    def parse(p):
        first = p.expect('ident')[1].lower()
        if first in ('safe', 'unsafe'):
            pos = p.expect('ident')[1].lower()
            if pos not in safe_keywords:
                raise Skip('%s %s' % (first, pos))
            if first == 'safe':
                return '%s.Safe%s' % (ty, pascal(pos))
            return 'new %s(%sKeyword.%s, AlignmentSafety.Unsafe)' % (ty, ty, pascal(pos))
        if first not in keywords:
            raise Skip('%s %s' % (ty, first))
        return '%s.%s' % (ty, pascal(first))
    return parse


def keyword_enum(ty, keywords):
    def parse(p):
        ident = p.expect('ident')[1].lower()
        if ident not in keywords:
            raise Skip('%s %s' % (ty, ident))
        return '%s.%s' % (ty, keywords[ident])
    return parse


def flex_wrap(p):
    wrap, balance, first = None, False, True
    while True:
        if first:
            ident = p.expect('ident')[1].lower()
        else:
            token = p.try_parse(lambda: p.expect('ident'))
            if token is None:
                break
            ident = token[1].lower()
        if ident == 'nowrap' and first:
            return 'FlexWrap.NoWrap'
        if ident in ('wrap', 'wrap-reverse') and wrap is None:
            wrap = ident
        elif ident == 'balance' and not balance:
            balance = True
        else:
            raise Skip('flex-wrap ' + ident)
        first = False
    if balance:
        return 'FlexWrap.BalanceReverse' if wrap == 'wrap-reverse' else 'FlexWrap.Balance'
    return 'FlexWrap.' + pascal(wrap)


def contain(p):
    flags, seen = [], set()
    while True:
        ident = p.expect('ident')[1].lower()
        if ident in ('none', 'content'):
            if seen or not p.exhausted():
                raise Skip('contain ' + ident)
            return 'TaffySharp.Contain.' + pascal(ident)
        if ident not in ('layout', 'paint', 'style') or ident in seen:
            raise Skip('contain ' + ident)
        seen.add(ident)
        if ident != 'style':
            flags.append('TaffySharp.Contain.' + pascal(ident))
        if p.exhausted():
            return ' | '.join(flags) or 'TaffySharp.Contain.None'


def grid_auto_flow(p):
    axis, dense = None, False
    for _ in range(2):
        token = p.try_parse(lambda: p.expect('ident'))
        if token is None:
            break
        if token[1] in ('row', 'column'):
            axis = token[1]
        elif token[1] == 'dense':
            dense = True
        else:
            raise Skip('grid-auto-flow ' + token[1])
    if axis is None and not dense:
        raise Skip('grid-auto-flow')
    return 'GridAutoFlow.' + pascal(axis or 'row') + ('Dense' if dense else '')


def available_space(p):
    token = p.next()
    if token[0] in ('num', 'dim') and token[1] >= 0:
        return 'AvailableSpace.Definite(%s)' % lit(token[1])
    if token in (('ident', 'max-content'), ('ident', 'min-content')):
        return 'AvailableSpace.' + pascal(token[1])
    raise Skip('available space %r' % (token,))


DISPLAY = keyword_enum('Display', {'none': 'None', 'flex': 'Flex', 'grid': 'Grid', 'block': 'Block', 'flow-root': 'FlowRoot'})
POSITION = keyword_enum('Position', {k: pascal(k) for k in ('static', 'relative', 'absolute', 'fixed', 'sticky')})
BOX_SIZING = keyword_enum('BoxSizing', {'border-box': 'BorderBox', 'content-box': 'ContentBox'})
OVERFLOW = keyword_enum('Overflow', {k: pascal(k) for k in ('visible', 'hidden', 'clip', 'scroll')})
DIRECTION = keyword_enum('Direction', {'ltr': 'Ltr', 'rtl': 'Rtl'})
FLEX_DIRECTION = keyword_enum('FlexDirection', {k: pascal(k) for k in ('row', 'column', 'row-reverse', 'column-reverse')})
FLOAT = keyword_enum('TaffySharp.Float', {k: pascal(k) for k in ('left', 'right', 'none')})
CLEAR = keyword_enum('Clear', {k: pascal(k) for k in ('left', 'right', 'both', 'none')})
TEXT_ALIGN = keyword_enum('TextAlign', {'auto': 'Auto', '-webkit-left': 'LegacyLeft', '-webkit-right': 'LegacyRight',
                                        '-webkit-center': 'LegacyCenter'})
ALIGN_ITEMS_P = alignment('AlignItems', ALIGN_ITEMS, SAFE_ITEMS)
ALIGN_CONTENT_P = alignment('AlignContent', ALIGN_CONTENT, SAFE_CONTENT)


def build_style(a):
    """Mirrors build_style in upstream tests/xml.rs; only properties given in the XML are emitted."""
    props = []

    def get(name, fn):
        return parse_entirely(a[name], fn) if name in a else None

    def simple(prop, name, fn):
        value = get(name, fn)
        if value is not None:
            props.append('%s = %s' % (prop, value))

    def pair(prop, ty, names, fn, default):
        values = [get(n, fn) for n in names]
        if any(v is not None for v in values):
            props.append('%s = new %s(%s)' % (prop, ty, ', '.join(v or default for v in values)))

    lpa = lambda p: length_percentage(p, 'LengthPercentageAuto', ('auto',))
    lp = lambda p: length_percentage(p, 'LengthPercentage')

    simple('Display', 'display', DISPLAY)
    simple('Direction', 'direction', DIRECTION)
    simple('BoxSizing', 'box-sizing', BOX_SIZING)
    pair('Overflow', 'Point<Overflow>', ['overflow-x', 'overflow-y'], OVERFLOW, 'Overflow.Visible')
    if 'scrollbar-width' in a:
        props.append('ScrollbarWidth = ' + lit(parse_f32(a['scrollbar-width'])))
    simple('Contain', 'contain', contain)
    simple('Float', 'float', FLOAT)
    simple('Clear', 'clear', CLEAR)
    simple('Position', 'position', POSITION)
    pair('Size', 'Size<Dimension>', ['width', 'height'], dimension, 'Dimension.Auto')
    pair('MinSize', 'Size<LengthPercentageAuto>', ['min-width', 'min-height'], lpa, 'LengthPercentageAuto.Auto')
    pair('MaxSize', 'Size<LengthPercentageAuto>', ['max-width', 'max-height'], lpa, 'LengthPercentageAuto.Auto')
    sides = ['left', 'right', 'top', 'bottom']
    pair('Inset', 'Rect<LengthPercentageAuto>', sides, lpa, 'LengthPercentageAuto.Auto')
    pair('Margin', 'Rect<LengthPercentageAuto>', ['margin-' + s for s in sides], lpa, 'LengthPercentageAuto.Zero')
    pair('Padding', 'Rect<LengthPercentage>', ['padding-' + s for s in sides], lp, 'LengthPercentage.Zero')
    pair('Border', 'Rect<LengthPercentage>', ['border-' + s for s in sides], lp, 'LengthPercentage.Zero')
    pair('Gap', 'Size<LengthPercentage>', ['column-gap', 'row-gap'], lp, 'LengthPercentage.Zero')
    if 'aspect-ratio' in a:
        try:
            props.append('AspectRatio = ' + lit(parse_f32(a['aspect-ratio'])))
        except Skip:
            pass  # maybe_parse: an unparsable aspect-ratio is ignored
    simple('AlignItems', 'align-items', ALIGN_ITEMS_P)
    for prop, name in (('AlignSelf', 'align-self'), ('JustifySelf', 'justify-self')):
        if name in a and a[name].strip().lower() != 'auto':
            simple(prop, name, ALIGN_ITEMS_P)
    simple('JustifyItems', 'justify-items', ALIGN_ITEMS_P)
    simple('AlignContent', 'align-content', ALIGN_CONTENT_P)
    simple('JustifyContent', 'justify-content', ALIGN_CONTENT_P)
    simple('TextAlign', 'text-align', TEXT_ALIGN)
    simple('FlexDirection', 'flex-direction', FLEX_DIRECTION)
    simple('FlexWrap', 'flex-wrap', flex_wrap)
    if 'flex-line-count' in a:
        if not re.fullmatch(r'\+?\d+', a['flex-line-count']) or int(a['flex-line-count']) > 65535:
            raise Skip('flex-line-count')
        props.append('FlexLineCount = %d' % int(a['flex-line-count']))
    for prop, name in (('FlexGrow', 'flex-grow'), ('FlexShrink', 'flex-shrink')):
        if name in a:
            props.append('%s = %s' % (prop, lit(parse_f32(a[name]))))
    simple('FlexBasis', 'flex-basis', dimension)
    simple('GridAutoFlow', 'grid-auto-flow', grid_auto_flow)
    for axis in ('rows', 'columns'):
        name = 'grid-template-' + axis
        if name in a:
            tracks, names = parse_entirely(a[name], lambda p: template_tracks(p, template_component))
            props.append('GridTemplate%s = %s' % (axis.capitalize(), list_expr(tracks, 'GridTemplateComponent')))
            props.append('GridTemplate%sNames = %s' % (axis[:-1].capitalize(), names_expr(names)))
        name = 'grid-auto-' + axis
        if name in a:
            def auto_tracks(p):
                tracks = []
                while not p.exhausted():
                    tracks.append(track_sizing_function(p))
                if not tracks:
                    raise Skip('empty grid-auto tracks')
                return tracks
            props.append('GridAuto%s = %s' % (axis.capitalize(), list_expr(get(name, auto_tracks), 'TrackSizingFunction')))
    for axis in ('row', 'column'):
        pair('Grid' + axis.capitalize(), 'Line<GridPlacement>', ['grid-%s-start' % axis, 'grid-%s-end' % axis], grid_placement,
             'GridPlacement.Auto')
    return 'new Style()' if not props else 'new Style\n{\n    %s,\n}' % ',\n    '.join(props)


def expectation(x, indent):
    def num(name):
        if name not in x.attrib:
            raise Skip('expectation without ' + name)
        return lit(parse_f32(x.attrib[name]))

    head = 'new ExpectedNode(%s, %s, %s, %s)' % (num('x'), num('y'), num('width'), num('height'))
    props = []
    try:
        props.append('ScrollSize = new Size<float>(%s, %s)' % (lit(parse_f32(x.attrib.get('scroll_width'))),
                                                                lit(parse_f32(x.attrib.get('scroll_height')))))
    except Skip:
        pass
    for prop, name in (('ResolvedRows', 'resolved-rows'), ('ResolvedColumns', 'resolved-columns')):
        if name in x.attrib:
            props.append('%s = %s' % (prop, cs_string(x.attrib[name])))
    children = [c for c in x if c.tag == 'node']
    if children:
        inner = indent + '        '
        props.append('Children =\n%s    {\n%s,\n%s    }' % (indent, ',\n'.join(inner + expectation(c, inner) for c in children), indent))
    if not props:
        return head
    return '%s\n%s{\n%s    %s,\n%s}' % (head, indent, indent, (',\n' + indent + '    ').join(props), indent)


def convert(path, group, name):
    test = ET.parse(path).getroot()
    use_rounding = test.attrib.get('use-rounding', 'true')
    if use_rounding not in ('true', 'false'):
        raise Skip('use-rounding')
    viewport, inp, exp = (test.find(tag) for tag in ('viewport', 'input', 'expectations'))
    space = [parse_entirely(viewport.attrib[n], available_space) if n in viewport.attrib else 'AvailableSpace.MaxContent'
             for n in ('width', 'height')]

    lines = ['TaffyTree<TestNodeContext> taffy = new();']
    if use_rounding == 'false':
        lines.append('taffy.DisableRounding();')
    counter = [0]

    def construct(node, expected_node, parent):
        var = 'node%d' % counter[0]
        counter[0] += 1
        style = build_style(node.attrib).replace('\n', '\n' + ' ' * 0)
        children = [c for c in node if isinstance(c.tag, str)]
        if children:
            lines.append('NodeId %s = taffy.NewWithChildren(%s);' % (var, style))
            if parent:
                lines.append('taffy.AddChild(%s, %s);' % (parent, var))
            for child, child_expected in zip(children, [c for c in expected_node if isinstance(c.tag, str)]):
                construct(child, child_expected, var)
        else:
            lines.append('NodeId %s = taffy.NewLeaf(%s);' % (var, style))
            if node.text is not None:
                mode = node.attrib.get('writing-mode')
                if mode is None:
                    writing_mode = 'Horizontal'
                elif 'vertical' in mode:
                    writing_mode = 'Vertical'
                elif 'horizontal' in mode:
                    writing_mode = 'Horizontal'
                else:
                    raise Skip('writing-mode')
                lines.append('taffy.SetNodeContext(%s, TestNodeContext.AhemText(%s, WritingMode.%s));'
                             % (var, cs_string(node.text.strip()), writing_mode))
            if parent:
                lines.append('taffy.AddChild(%s, %s);' % (parent, var))

    construct(inp.find('*'), exp.find('*'), None)
    lines.append('taffy.ComputeLayoutWithMeasure(node0, new Size<AvailableSpace>(%s, %s), MeasureFunction);' % tuple(space))
    lines.append('ExpectedNode.AssertLayout(taffy, node0, %s);' % expectation(exp.find('*'), ''))
    body = '\n'.join(lines).replace('\n', '\n        ')
    return ('// Generated by tests/tools/convert_generated.py from taffy tests/xml/%s/%s.xml. Do not edit.\n'
            'using static TaffySharp.Tests.TestHelpers;\n\n'
            'namespace TaffySharp.Tests.Generated;\n\n'
            'public partial class %s\n{\n    [Fact]\n    public void %s()\n    {\n        %s\n    }\n}\n'
            % (group, name, GROUPS[group], name, body))


def main():
    src, dst = sys.argv[1], sys.argv[2]
    total = collections.Counter()
    skipped = collections.Counter()
    for group, cls in GROUPS.items():
        out_dir = os.path.join(dst, cls)
        shutil.rmtree(out_dir, ignore_errors=True)
        os.makedirs(out_dir)
        for file in sorted(os.listdir(os.path.join(src, group))):
            if not file.endswith('.xml'):
                continue
            name = file[:-4]
            total[cls] += 1
            try:
                code = convert(os.path.join(src, group, file), group, name)
            except Skip as e:
                skipped[(cls, str(e))] += 1
                continue
            with open(os.path.join(out_dir, name + '.cs'), 'w', encoding='utf-8', newline='\n') as f:
                f.write(code)
    for cls in GROUPS.values():
        n = sum(v for (c, _), v in skipped.items() if c == cls)
        print('%-10s converted %4d of %4d' % (cls, total[cls] - n, total[cls]))
    for (cls, reason), n in skipped.most_common():
        print('  skipped %s: %d x %s' % (cls, n, reason))


if __name__ == '__main__':
    main()
