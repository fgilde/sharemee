import re, sys, os
plan, task, step = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
text = open(plan, encoding='utf-8').read()
tasks = re.split(r'\n### Task (\d+):', text)
body = dict(zip(map(int, tasks[1::2]), tasks[2::2]))[task]
steps = re.split(r'\n- \[ \] \*\*Step (\d+):', body)
sbody = dict(zip(map(int, steps[1::2]), steps[2::2]))[step]
base = r'C:\dev\privat\github'
for m in re.finditer(r'File: (\S+)\n```[a-z]*\n(.*?)\n```', sbody, re.S):
    path = os.path.join(base, m.group(1).replace('/', os.sep))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, 'w', encoding='utf-8', newline='\n').write(m.group(2) + '\n')
    print('wrote', m.group(1))
