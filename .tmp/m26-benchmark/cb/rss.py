import subprocess,resource,sys
p=subprocess.run(sys.argv[1:],capture_output=True,text=True,timeout=120)
print(p.stdout.strip(), '| maxRSS', resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss//1024,'MB')
