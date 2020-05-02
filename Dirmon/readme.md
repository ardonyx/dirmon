# Dirmon

A tool for monitoring a single directory for file modifications. This is tool is specifically designed to capture 
ephemeral state that changes to quickly to easily capture with other tools. As an example, during the installation 
or launch of certain programs, temporary files may be quickly created and deleted. This tool allows you to quickly
pull the contents into memory which can printed to console or written to disk at a later time. 

## Why Dirmon 

There are many tools for monitoring system changes during application runtime and installation. However, they are 
either difficult to configure correctly or don't quite capture the _contents_ of the files which is what we're 
interested in. 

## Usage 

Dirmon takes requires to parameters:

1) Monitor : the directory to monitor. This is a non-recursive directory monitor that will be watched for any file 
changes. 
2) Shadow : the directory that receives file state changes. Every time a file is modified, a snapshot of its contents 
are written to this directory with a sequence number.

In the event of a file change, Dirmon will attempt to open the file in read-share mode to capture the contents into 
memory. Once captured, the contents are passed to a background thread for processing. This is because some files exist 
for a very short amount of time and may be missed if the read process takes too long. 

### Other Features 

* Purge Shadow directory : Set this flag to clear the previous shadow directory of snapshots. Defaults to off. 
* Display Binary : A best-effort is made to not display the contents of binary files on the console. Set this flag to 
true to print binaries to your console. 