import os
log_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '_test_result.txt')
with open(log_path, 'w') as f:
    f.write('qrenderdoc python works!\n')
    import sys
    f.write('Python version: ' + sys.version + '\n')
    try:
        f.write('rd type: ' + str(type(rd)) + '\n')
    except NameError:
        f.write('rd: NOT available\n')
    try:
        import renderdoc
        f.write('renderdoc module: available\n')
    except ImportError:
        f.write('renderdoc module: NOT available\n')
os._exit(0)
