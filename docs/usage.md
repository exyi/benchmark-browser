## Home

Page located at `#home`.

You should be able to see two columns with data - the tested repositories and task definitions. It shows some basic statistics about each of these, e.g. how much reports were collected for the repository or task definition. You can click on them to navigate to the project or task definition summary.

## Project Dashboard

The page is located at `#board/<project id>` where project id a root commit of the git repository. The project is not declared anywhere in the database, it's just a group of test reports that happened to be related to the same git repository. To identify repositories the system uses root commit hash, as it will group forks into one project and it's pretty unlikely to have two repositories with the same root commit in one system. (There is a little caveat with repositories that have multiple roots, which is maybe possible, then the id should contain both of them, but I'm not going to test that)

There is a list of related task definitions for quick navigation.

Then there is a chart that contains the longest path in the DAG of tested commits, with plotted comparison with latest tested commit (means "latest commit that is tested", not "commit with newest test results"). You can hover the chart to see a brief commit description or click on it to jump to a comparison (of the clicked version with latest tested commit). The compared number is a triple of the minimal, average and maximal difference of median time between these versions.

Under the chart, there is a table of "branches" which contain a comparison of latest commit on each branch. You can click on each to get to a comparison with the latest tested commit. Note that sorting the tested versions into "branches" is pretty tricky in git, as there is no information in a commit on which branch it was committed. So there is an algorithm that tries to guess that from names in merge commits, the order of parents in the merge commits and the current state of heads on the remote repository.

The last table on this page is just a list of all test runs, click on the commit hash to get to the version detail.

## Task Dashboard

This page currently only contains a list of related projects and an "Enqueue" button, if have sufficient rights. I did not feel any need put there any detailed info.

## Version Detail and Comparison

Comparison is at `#compare/commits/<base version>/<target version>` and detail at `#detail/commit/ce62efd1c7903fb97f99b96138cb714e07acd3aa`. Feel free to edit these urls manually if you want to compare specific versions, the URL is part of the UI here ;). I think it's more ergonomic than a dialog that would ask for the two versions...

The page contains a brief description of compared commits (with a "Swap" and "Detail" buttons), summary table of the comparison and a table with all tests that the two versions share.

Summary table contains the time and memory comparison (minimum, average and maximum of median) for detected groups in the list of tests. The first one is always a group of all tests, then there are groups by benchmark class and method, if they contain more than one element.

Then there is the grid with all the data. It probably contains a lot of columns, so you can remove them using the menu in the column header or remove them by groups using the "Remove columns menu". Add them using the "Add columns" menu. It's also possible to reorder them by drag and drop. It's also possible to sort and filter them in the column header menu. The filter and sort should appear above the grid, you can edit or remove them here. Note that the settings are persisted in localStorage, so you don't have to reconfigure the table every time.

By the way, if you don't like the empty borders on around the grid, there is a "Full width" switch on the top of the page.

Above the grid, there is a menu called "Attached files" and it is used to access data attached to the tests - you can download them in a `zip` archive or view an aggregated [flame graph](https://github.com/BrendanGregg/FlameGraph) if it's from a profiler. The flamegraph url is `files/flame?<file ids>&q_width=<width of the result svg>` and you are supposed to manipulate this URL ;) - you can exclude/include certain tests in the benchmark and it has a few optional parameters:

* `q_title` - changes the title of the flame graph, useful if you want to share it with someone
* `q_colors` - change the colors, according to [FlameGraph](https://github.com/BrendanGregg/FlameGraph) docs - set color palette. choices are: hot (default), mem, io, wakeup, chain, java, js, perl, red, green, blue, aqua, yellow, purple, orange
* `q_dig` - show stack frames that start with the specified function (specified by substring, you don't have to enter full name)

Note that the sample counts in the flamegraph are adjusted, so all tests are represented equally.

## Administration

The system also contains some administation for adding tasks, queuing them, adding user and so on... It's pretty rough and undocumented :P

