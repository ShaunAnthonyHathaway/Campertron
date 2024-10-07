<div align="center">
<a href="https://github.com/ShaunAnthonyHathaway/Campertron">
  <img src="https://github.com/ShaunAnthonyHathaway/Campertron/blob/master/docs/undraw_camping_noc8.svg"
    width="400" height="400" alt="campertron">
</a>
</div>
Campertron is a free campsite tracking application that can help you find available campsites on recreation.gov as they become available. You can run on most platforms or as a container and setup email notifications when a site is open for booking.  Plans change and campsites open up constantly but if you aren't watching closely you will miss it.  Use Campertron so you don't miss those opportunities to make it to your desired destination!

---

## Download and Run

1. Grab the [release for your platform](https://github.com/ShaunAnthonyHathaway/Campertron/releases/tag/v0.1)
2. Extract, Run CampertronConsole, and enjoy!

<a href="https://github.com/ShaunAnthonyHathaway/Campertron">
  <img src="https://github.com/ShaunAnthonyHathaway/Campertron/blob/master/docs/ss1.png"
    width="1102" height="625" alt="campertron">

<a href="https://github.com/ShaunAnthonyHathaway/Campertron">
  <img src="https://github.com/ShaunAnthonyHathaway/Campertron/blob/master/docs/ss2.png"
    width="1105" height="625" alt="campertron">    

## Configure

The first time you run Campertron a sample configuration file is automatically generated for you.  Multiple configuration files can be used and Campertron will search against them all when it runs.  Campertron will only grab the data it needs for all of your configuration files once and it can search against them in parallel.  You can view the directory path that the configuration files are stored in with the **View Config** menu option.

<a href="https://github.com/ShaunAnthonyHathaway/Campertron">
  <img src="https://github.com/ShaunAnthonyHathaway/Campertron/blob/master/docs/ss4.png"
    width="1101" height="624" alt="campertron">    

<a href="https://github.com/ShaunAnthonyHathaway/Campertron">
  <img src="https://github.com/ShaunAnthonyHathaway/Campertron/blob/master/docs/ss5.png"
    width="1105" height="627" alt="campertron"> 

Example configurations and instructions can be [found here](https://github.com/ShaunAnthonyHathaway/Campertron/tree/master/examples/0.1)

Additionally you can run CampertronGUI to get the campground ID and other information about the locations.

<a href="https://github.com/ShaunAnthonyHathaway/Campertron">
  <img src="https://github.com/ShaunAnthonyHathaway/Campertron/blob/master/docs/ss6.png"
    width="1167" height="728" alt="campertron">

## Docker

1. Follow the [Docker official installation guide](https://docs.docker.com/engine/install)
2. Download the latest container image.

```commandline
docker pull shaunhathaway/campertronconsole
```

3. Create either persistent storage or persistent volumes for the configuration and cache data.

Persistent storage:

```commandline
mkdir /path/to/config
mkdir /path/to/cache
```

Persistent Volume:

```commandline
docker volume create campertron-config
docker volume create campertron-cache
```
4. Create and run the container:

Persistent storage:

```commandline
docker run -d --name campertron --volume /path/to/config:/config --volume /path/to/cache:/cache shaunhathaway/campertronconsole
```

Persistent Volume:

```commandline
docker run -d --name campertron --volume campertron-config:/config --volume campertron-cache:/cache shaunhathaway/campertronconsole
```
