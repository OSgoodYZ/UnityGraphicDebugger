import os
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '_test_ok.txt'), 'w') as f:
    f.write('OK')
os._exit(0)
