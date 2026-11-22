# Builds .github/media/architecture.svg (animated, for the README) and web/public/architecture.html
# (interactive) from one list of boxes and arrows. Run from the repo root:
#   python3 .github/media/architecture/make.py
import json, html
NW,NH = 280,100
G=230
X1=40; X2=X1+NW+G; X3=X2+NW+G
M1=X1+NW+G//2; M2=X2+NW+G//2
W,H = X3+NW+40, 760
C = {'visitor':'#60a5fa','host':'#2dd4bf','data':'#a78bfa','ai':'#fb923c','deploy':'#facc15','guard':'#f87171'}
# id, tag, cat, title, sub, x, y, detail
N = [
 ('browser','VISITOR','visitor','Browser','phone, tablet, desktop',X1,250,'Anyone with the link. No account and no app to install. Friends tap what they had; the browser never calculates money.'),
 ('swa','HOST','host','Static Web Apps','Vue 3 app, Free plan',X2,90,'Serves the built Vue app. Free plan, so hosting costs nothing. Sends noindex and no-referrer headers.'),
 ('api','API','host','Container Apps','.NET 10 minimal API',X2,330,'Owns all money maths and returns computed breakdowns. Consumption plan, 0 to 1 replicas, so it sleeps when nobody uses it. Rate limits and daily caps on creates and scans.'),
 ('blob','DATA','data','Blob Storage','one JSON blob per bill',X3,170,'Each bill is one JSON blob. ETag checks stop two people from overwriting each other. A lifecycle rule deletes bills 30 days after the last change.'),
 ('di','SCAN','ai','Document Intelligence','receipt model, F0',X3,330,'Reads receipt photos on the free F0 tier. The photo stays in memory for one request, and the API asks for it to be deleted right after.'),
 ('kill','COST GUARD','guard','Budget + kill switch','$1 a month cap',X3,520,'A $1 monthly budget. When actual spend passes 100%, an action group runs a runbook that stops the API until someone turns it back on.'),
 ('actions','DEPLOY','deploy','GitHub Actions','tests, Bicep, web upload',X1,560,'One manual workflow. Tests run first; deploys sign in to Azure with OIDC, so no keys are stored.'),
 ('ghcr','IMAGE','deploy','Container Registry','public API image',X2,560,'The API image, tagged with its commit. Public, so Azure pulls it with no credential.'),
]
nodes = {n[0]:n for n in N}
# id, from, to, label, path points, label pos
E = [
 ('e1','browser','swa','loads the app',[(X1+NW,280),(M1,280),(M1,140),(X2,140)],(M1,210)),
 ('e2','browser','api','JSON over HTTPS',[(X1+NW,320),(M1,320),(M1,380),(X2,380)],(M1,350)),
 ('e3','api','blob','bills, ETag checks',[(X2+NW,350),(M2,350),(M2,220),(X3,220)],(M2,285)),
 ('e4','api','di','photo, not stored',[(X2+NW,380),(X3,380)],(M2,380)),
 ('e5','actions','ghcr','push image',[(X1+NW,610),(X2,610)],(M1,610)),
 ('e6','ghcr','api','pull image',[(X2+NW//2,560),(X2+NW//2,430)],(X2+NW//2,495)),
 ('e7','kill','api','stops at 100%',[(X3,570),(M2+20,570),(M2+20,410),(X2+NW,410)],(M2+20,490)),
]
STEPS = [
 (['e1'],['browser','swa'],'1 · A friend opens the link. Static Web Apps serves the Vue app.'),
 (['e2'],['browser','api'],'2 · Every claim is a small JSON call to the API, which returns the bill and its computed breakdown.'),
 (['e3'],['api','blob'],'3 · The API saves the bill as one JSON blob. ETag checks stop two people overwriting each other.'),
 (['e4'],['api','di'],'4 · Scanning sends the photo to Document Intelligence. It is never stored.'),
 (['e5','e6'],['actions','ghcr','api'],'5 · A manual deploy runs the tests, pushes the image, and the API pulls it.'),
 (['e7'],['kill','api'],'6 · If spend ever passes the $1 budget, the kill switch stops the API.'),
]
def d(pts): return 'M'+' L'.join(f'{x},{y}' for x,y in pts)
def esc(s): return html.escape(s)

def svg(interactive):
    o=[]
    o.append(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-label="Receipt Splitter architecture" font-family="ui-sans-serif, -apple-system, Segoe UI, Roboto, sans-serif">')
    o.append('''<defs>
<pattern id="dots" width="24" height="24" patternUnits="userSpaceOnUse"><circle cx="2" cy="2" r="1.2" fill="#2a2f3a"/></pattern>''')
    for k,v in C.items():
        o.append(f'<marker id="a-{k}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="9" markerHeight="9" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="{v}"/></marker>')
    o.append('</defs>')
    o.append('''<style>
.edge{fill:none;stroke-width:2.5}
.flow{fill:none;stroke-width:2.5;stroke-dasharray:6 10;animation:march 1.2s linear infinite;opacity:.9}
@keyframes march{to{stroke-dashoffset:-32}}
.mono{font-family:ui-monospace, SFMono-Regular, Menlo, Consolas, monospace}
@media (prefers-reduced-motion: reduce){.flow{animation:none}}
</style>''')
    o.append(f'<rect x="0" y="0" width="{W}" height="{H}" rx="18" fill="#12151b"/>')
    o.append(f'<rect x="0" y="0" width="{W}" height="{H}" rx="18" fill="url(#dots)"/>')
    o.append('<text x="40" y="52" class="mono" font-size="20" fill="#c9ced8">How a bill moves through Receipt Splitter</text>')
    # edges
    for eid,f,t,lab,pts,lp in E:
        col=C[nodes[f][2]]
        o.append(f'<g class="e" id="{eid}" data-from="{f}" data-to="{t}">')
        o.append(f'<path class="edge" d="{d(pts)}" stroke="{col}" stroke-opacity=".55" marker-end="url(#a-{nodes[f][2]})"/>')
        o.append(f'<path class="flow" d="{d(pts)}" stroke="{col}"/>')
        if not interactive:
            o.append(f'<circle r="5" fill="{col}"><animateMotion dur="2.4s" repeatCount="indefinite" path="{d(pts)}"/></circle>')
        else:
            o.append(f'<circle class="dot" r="5" fill="{col}" opacity="0"><animateMotion dur="1.6s" repeatCount="indefinite" path="{d(pts)}"/></circle>')
        x,y=lp; w=len(lab)*8.6+26
        o.append(f'<rect x="{x-w/2:.0f}" y="{y-15}" width="{w:.0f}" height="30" rx="15" fill="#12151b" stroke="{col}" stroke-width="1.5"/>')
        o.append(f'<text x="{x}" y="{y+5}" text-anchor="middle" class="mono" font-size="14" fill="#e6e9ef">{esc(lab)}</text>')
        o.append('</g>')
    for nid,tag,cat,title,sub,x,y,_ in N:
        col=C[cat]
        o.append(f'<g class="n" id="n-{nid}" data-id="{nid}" tabindex="{0 if interactive else -1}">')
        o.append(f'<rect x="{x}" y="{y}" width="{NW}" height="{NH}" rx="12" fill="#1b1f27" stroke="{col}" stroke-width="2.5"/>')
        o.append(f'<circle cx="{x+24}" cy="{y+27}" r="5" fill="{col}"/>')
        o.append(f'<text x="{x+38}" y="{y+32}" class="mono" font-size="14" letter-spacing="1.5" fill="{col}">{esc(tag)}</text>')
        o.append(f'<text x="{x+20}" y="{y+62}" font-size="22" font-weight="600" fill="#f3f5f8">{esc(title)}</text>')
        o.append(f'<text x="{x+20}" y="{y+86}" font-size="16" fill="#aab1bd">{esc(sub)}</text>')
        o.append('</g>')
    if not interactive:
        o.append(f'<text x="40" y="{H-30}" font-size="17" fill="#aab1bd">Dashed lines show the direction of each call. The interactive version steps through the flow.</text>')
    o.append('</svg>')
    return '\n'.join(o)

open('.github/media/architecture.svg','w').write(svg(False)+'\n')
data = {'nodes':{n[0]:{'title':n[3],'detail':n[7]} for n in N},'steps':[{'edges':s[0],'nodes':s[1],'text':s[2]} for s in STEPS]}
page = open('.github/media/architecture/page.template.html').read().replace('%%SVG%%', svg(True)).replace('%%DATA%%', json.dumps(data))
open('web/public/architecture.html','w').write(page)
